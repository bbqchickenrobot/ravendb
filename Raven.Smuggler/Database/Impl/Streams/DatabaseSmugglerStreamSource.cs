﻿// -----------------------------------------------------------------------
//  <copyright file="IDatabaseSmugglerStreamSource.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Indexing;
using Raven.Abstractions.Json;
using Raven.Abstractions.Smuggler;
using Raven.Abstractions.Smuggler.Data;
using Raven.Abstractions.Util;
using Raven.Imports.Newtonsoft.Json;
using Raven.Json.Linq;

namespace Raven.Smuggler.Database.Impl.Streams
{
	public class DatabaseSmugglerStreamSource : IDatabaseSmugglerSource
	{
		private readonly Stream _stream;

		private CountingStream _sizeStream;

		private StreamReader _streamReader;

		private JsonTextReader _reader;

		private DatabaseSmugglerOptions _options;

		public DatabaseSmugglerStreamSource(Stream stream)
		{
			_stream = stream;
		}

		public string DisplayName { get; set; }

		public bool SupportsMultipleSources => false;

		public IReadOnlyList<IDatabaseSmugglerSource> Sources => null;

		public Task InitializeAsync(DatabaseSmugglerOptions options, CancellationToken cancellationToken)
		{
			_options = options;

			try
			{
				_stream.Position = 0;
				_sizeStream = new CountingStream(new GZipStream(_stream, CompressionMode.Decompress));
				_streamReader = new StreamReader(_sizeStream);

				_reader = new RavenJsonTextReader(_streamReader);
			}
			catch (Exception e)
			{
				if (e is InvalidDataException == false)
					throw;

				_stream.Seek(0, SeekOrigin.Begin);

				_sizeStream = new CountingStream(_stream);

				_streamReader = new StreamReader(_sizeStream);

				_reader = new JsonTextReader(_streamReader);
			}

			if (_reader.Read() == false)
				return new CompletedTask();

			if (_reader.TokenType != JsonToken.StartObject)
				throw new InvalidDataException("StartObject was expected");

			return new CompletedTask();
		}

		public Task<List<IndexDefinition>> ReadIndexesAsync(int start, int pageSize, CancellationToken cancellationToken)
		{
			var results = new List<IndexDefinition>();

			while (_reader.Read() && _reader.TokenType != JsonToken.EndArray)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var index = (RavenJObject)RavenJToken.ReadFrom(_reader);
				if (_options.OperateOnTypes.HasFlag(ItemType.Indexes) == false)
					continue;

				var indexName = index.Value<string>("name");
				if (indexName.StartsWith("Temp/"))
					continue;

				var definition = index.Value<RavenJObject>("definition");
				if (definition.Value<bool>("IsCompiled"))
					continue; // can't import compiled indexes

				if (_options.OperateOnTypes.HasFlag(ItemType.RemoveAnalyzers))
					definition.Remove("Analyzers");

				var indexDefinition = definition.JsonDeserialization<IndexDefinition>();
				indexDefinition.Name = indexName;

				results.Add(indexDefinition);
			}

			return new CompletedTask<List<IndexDefinition>>(results);
		}

		public Task<LastEtagsInfo> FetchCurrentMaxEtagsAsync(CancellationToken cancellationToken)
		{
			return new CompletedTask<LastEtagsInfo>(new LastEtagsInfo
			{
				LastDocDeleteEtag = null,
				LastDocsEtag = null
			});
		}

		public Task<IAsyncEnumerator<RavenJObject>> ReadDocumentsAsync(Etag fromEtag, int pageSize, CancellationToken cancellationToken)
		{
			return new CompletedTask<IAsyncEnumerator<RavenJObject>>(new YieldJsonResults<RavenJObject>(fromEtag, _reader, cancellationToken, _options));
		}

		public Task<RavenJObject> ReadDocumentAsync(string key, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task<DatabaseStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public bool SupportsReadingDatabaseStatistics => false;

		public bool SupportsReadingHiLoDocuments => false;

		public bool SupportsDocumentDeletions => false;

		public bool SupportsPaging => false;

		public bool SupportsRetries => false;

		public Task<List<TransformerDefinition>> ReadTransformersAsync(int start, int batchSize, CancellationToken cancellationToken)
		{
			var results = new List<TransformerDefinition>();
			while (_reader.Read() && _reader.TokenType != JsonToken.EndArray)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var transformer = RavenJToken.ReadFrom(_reader);
				if (_options.OperateOnTypes.HasFlag(ItemType.Transformers) == false)
					continue;

				var transformerName = transformer.Value<string>("name");
				var definition = transformer.Value<RavenJObject>("definition");

				var transformerDefinition = definition.JsonDeserialization<TransformerDefinition>();
				transformerDefinition.Name = transformerName;

				results.Add(transformerDefinition);
			}

			return new CompletedTask<List<TransformerDefinition>>(results);
		}

		public Task<IAsyncEnumerator<string>> ReadDocumentDeletionsAsync(Etag fromEtag, Etag maxEtag, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task<List<KeyValuePair<string, long>>> ReadIdentitiesAsync(CancellationToken cancellationToken)
		{
			var results = new List<KeyValuePair<string, long>>();
			while (_reader.Read() && _reader.TokenType != JsonToken.EndArray)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var identity = RavenJToken.ReadFrom(_reader);

				var name = identity.Value<string>("Key");
				var value = identity.Value<long>("Value");

				results.Add(new KeyValuePair<string, long>(name, value));
			}

			return new CompletedTask<List<KeyValuePair<string, long>>>(results);
		}

		public Task<SmuggleType> GetNextSmuggleTypeAsync(CancellationToken cancellationToken)
		{
			while (_reader.Read() && _reader.TokenType != JsonToken.EndObject)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (_reader.TokenType != JsonToken.PropertyName)
					throw new InvalidDataException("PropertyName was expected");

				var currentSection = _reader.Value.ToString();

				if (_reader.Read() == false)
					return new CompletedTask<SmuggleType>(SmuggleType.None);

				if (_reader.TokenType != JsonToken.StartArray)
					throw new InvalidDataException("StartArray was expected");

				switch (currentSection)
				{
					case "Indexes":
						return new CompletedTask<SmuggleType>(SmuggleType.Index);
					case "Docs":
						return new CompletedTask<SmuggleType>(SmuggleType.Document);
					case "Attachments":
						return new CompletedTask<SmuggleType>(SmuggleType.Attachment);
					case "Transformers":
						return new CompletedTask<SmuggleType>(SmuggleType.Transformer);
					case "DocsDeletions":
						return new CompletedTask<SmuggleType>(SmuggleType.DocumentDeletion);
					case "AttachmentsDeletions":
						return new CompletedTask<SmuggleType>(SmuggleType.AttachmentDeletion);
					case "Identities":
						return new CompletedTask<SmuggleType>(SmuggleType.Identity);
					default:
						throw new NotSupportedException("Not supported section: " + currentSection);
				}
			}

			return new CompletedTask<SmuggleType>(SmuggleType.None);
		}

		public void Dispose()
		{
			_streamReader?.Dispose();

			_sizeStream?.Dispose();
		}

		private class YieldJsonResults<T> : IAsyncEnumerator<T>
			where T : RavenJToken
		{
			private readonly Etag _fromEtag;

			private readonly JsonTextReader _reader;

			private readonly CancellationToken _cancellationToken;

			private readonly DatabaseSmugglerOptions _options;

			public YieldJsonResults(Etag fromEtag, JsonTextReader reader, CancellationToken cancellationToken, DatabaseSmugglerOptions options)
			{
				_fromEtag = fromEtag;
				_reader = reader;
				_cancellationToken = cancellationToken;
				_options = options;
			}

			public void Dispose()
			{
				Current = null;
			}

			public Task<bool> MoveNextAsync()
			{
				Current = null;

				while (_reader.Read() && _reader.TokenType != JsonToken.EndArray)
				{
					_cancellationToken.ThrowIfCancellationRequested();

					var document = (RavenJObject)RavenJToken.ReadFrom(_reader);

					if (_options.OperateOnTypes.HasFlag(ItemType.Documents) == false)
						continue;

					var etag = Etag.Parse(document.Value<RavenJObject>("@metadata").Value<string>("@etag"));
					if (EtagUtil.IsGreaterThan(_fromEtag, etag))
						continue;

					Current = document as T;
					return new CompletedTask<bool>(true);
				}

				return new CompletedTask<bool>(false);
			}

			public T Current { get; private set; }
		}
	}
}