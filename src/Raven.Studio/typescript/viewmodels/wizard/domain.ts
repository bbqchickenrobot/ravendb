import setupStep = require("viewmodels/wizard/setupStep");
import router = require("plugins/router");
import claimDomainCommand = require("commands/wizard/claimDomainCommand");
import nodeInfo = require("models/wizard/nodeInfo");
import ipEntry = require("models/wizard/ipEntry");
import loadAgreementCommand = require("commands/wizard/loadAgreementCommand");

class domain extends setupStep {

    spinners = {
        save: ko.observable<boolean>(false)
    };
    
    canActivate(): JQueryPromise<canActivateResultDto> {
        const mode = this.model.mode();

        if (mode && mode === "LetsEncrypt") {
            return $.when({ can: true });
        }

        return $.when({ redirect: "#welcome" });
    }
    
    activate(args: any) {
        super.activate(args);

        const domainModel = this.model.domain();
        const userInfo = this.model.userDomains();
        if (userInfo) {
            domainModel.userEmail(userInfo.Email);
            domainModel.availableDomains(Object.keys(userInfo.Domains));

            if (domainModel.availableDomains().length === 1) {
                domainModel.domain(domainModel.availableDomains()[0]);
            }
        }
    }

    back() {
        router.navigate("#license");
    }
    
    save() {
        this.spinners.save(true);
        
        const domainModel = this.model.domain();
        this.afterAsyncValidationCompleted(domainModel.validationGroup, () => {
            if (this.isValid(domainModel.validationGroup)) {
                $.when<any>(this.claimDomainIfNeeded(), this.loadAgreementIfNeeded())
                    .done(() => {
                        this.tryPopulateNodesInfo();
                        router.navigate("#nodes");
                    })
                    .always(() => this.spinners.save(false));
            } else {
                this.spinners.save(false);
            }
        });
    }
    
    private tryPopulateNodesInfo() {
        this.model.domain().reusingConfiguration(false);
        const domains = this.model.userDomains();
        const chosenDomain = this.model.domain().domain();
        if (domains) {
            const existingDomainInfo = domains.Domains[chosenDomain];
            if (existingDomainInfo) {
                const nodes = existingDomainInfo.map(info => {
                    const entry = new nodeInfo(this.model.hostnameIsNotRequired);
                    entry.nodeTag(info.SubDomain);
                    entry.ips(info.Ips.map(x => ipEntry.forIp(x)));
                    return entry;
                });
                
                if (nodes.length > 0) {
                    this.model.domain().reusingConfiguration(true);
                    this.model.nodes(nodes);
                }
            }
        }
    }
    
    private loadAgreementIfNeeded(): JQueryPromise<void | string> {
        if (this.model.agreementUrl()) {
            return $.when<void>();
        }
        
        return new loadAgreementCommand(this.model.domain().userEmail())
            .execute()
            .done(url => {
                this.model.agreementUrl(url);
            });
    }
    
    private claimDomainIfNeeded(): JQueryPromise<void> {
        const domainModel = this.model.domain();
        
        if (_.includes(domainModel.availableDomains(), domainModel.domain())) {
            // no need to claim it
            return $.when<void>();
        }

        const domainToClaim = domainModel.domain();
        return new claimDomainCommand(domainToClaim, this.model.license().toDto())
            .execute()
            .done(() => {
                this.model.userDomains().Domains[domainToClaim] = [];
                domainModel.availableDomains.push(domainToClaim);
            });
    }

    createDomainNameAutocompleter(domainText: KnockoutObservable<string>) {
        return ko.pureComputed(() => {
            const key = domainText();
            const availableDomains = this.model.domain().availableDomains();
            
            if (key) {
                return availableDomains.filter(x => x.toLowerCase().includes(key.toLowerCase()));
            } else {
                return availableDomains;
            }           
        });    
    }
}

export = domain;
