using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Xero.Api.Core;
using Xero.Api.Core.Model;
using Xero.Api.Example.Applications.Private;
using Xero.Api.Example.Applications.Public;
using Xero.Api.Example.TokenStores;
using Xero.Api.Infrastructure.OAuth;
using Xero.Api.Serialization;

namespace XeroInvoiceIntegration
{
    class XeroIntegration
    {

        public Contact CreateContact(Contact newContact)
        {
            // Private Application Sample
            X509Certificate2 cert = new X509Certificate2(@"E:\Projects\SouthPaw\Development\public_privatekey.pfx", "Spectere99");
            var private_app_api = new XeroCoreApi("https://api.xero.com", new PrivateAuthenticator(cert),
                new Consumer("ELKNE2VQNWH2RQQG3OV4KKOI4JMNGZ", "PKLBNCKBQSCOEXLCMG7IHYPPSSPQKO"), null,
                new DefaultMapper(), new DefaultMapper());

            var user = new ApiUser { Name = Environment.MachineName };

            var org = private_app_api.Organisation;

            var contacts = private_app_api.Contacts.Find().ToList();

            Contact returnContact = newContact;
            if (!contacts.Exists(p => p.Name.Equals(newContact.Name)))
            {
                //See if contact exists
                returnContact = private_app_api.Create(newContact);
            }

            return returnContact;

            //Create Contact


            // Public Application Sample
            //var public_app_api = new XeroCoreApi("https://api.xero.com", new PublicAuthenticator("https://api.xero.com", "https://api.xero.com", "oob",
            //    new MemoryTokenStore()),
            //    new Consumer("F9WJSI6VC9TFJSOA6GPO6DFV8GFH1R", "KZZKTVEISETLPKXDAM3ZB7HQJTRQLM"), user,
            //    new DefaultMapper(), new DefaultMapper());

            //var public_contacts = public_app_api.Contacts.Find().ToList();

        }
    }
}
