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
            X509Certificate2 cert = new X509Certificate2(@"E:\Projects\SouthPaw\Development\public_privatekey.pfx",
                "Spectere99");
            var private_app_api = new XeroCoreApi("https://api.xero.com", new PrivateAuthenticator(cert),
                new Consumer("JSWZ2I77HEVJN3MVPAFHLXURUQO82E", "43W5TPJNATD6JE2XLSQUPPZMG4PPLG"), null,
                new DefaultMapper(), new DefaultMapper());

            var user = new ApiUser {Name = Environment.MachineName};

            var org = private_app_api.Organisation;

            //var contacts = private_app_api.Contacts.Find().ToList();

            var foundContact =
                private_app_api.Contacts.Where(string.Format("Name == \"{0}\"", newContact.Name))
                    .Find()
                    .FirstOrDefault();
            Contact returnContact = newContact;
            if (foundContact != null)
            {
                //Update Contact
                newContact.Id = foundContact.Id;
                returnContact = private_app_api.Contacts.Update(newContact);
            }
            else //New Contact
            {
                returnContact = private_app_api.Contacts.Create(newContact);
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

        public Invoice CreateInvoice(Invoice newInvoice)
        {
            X509Certificate2 cert = new X509Certificate2(@"E:\Projects\SouthPaw\Development\public_privatekey.pfx",
                "Spectere99");
            var private_app_api = new XeroCoreApi("https://api.xero.com", new PrivateAuthenticator(cert),
                new Consumer("JSWZ2I77HEVJN3MVPAFHLXURUQO82E", "43W5TPJNATD6JE2XLSQUPPZMG4PPLG"), null,
                new DefaultMapper(), new DefaultMapper());

            var user = new ApiUser {Name = Environment.MachineName};

            var org = private_app_api.Organisation;

            var foundInvoice =
                private_app_api.Invoices.Where(string.Format("InvoiceNumber == \"{0}\"", newInvoice.Number))
                    .Find()
                    .FirstOrDefault();
            Invoice returnInvoice = newInvoice;
            if (foundInvoice != null)
            {
                //Do we need to update the invoice if it is already created or do we just report it????
                newInvoice.Id = newInvoice.Id;
                //returnContact = private_app_api.Contacts.Update(newContact);
            }
            else //New Contact
            {
                returnInvoice = private_app_api.Invoices.Create(newInvoice);
            }
            return returnInvoice;
        }

        public Payment CreatePayment(Payment newPayment)
        {
            X509Certificate2 cert = new X509Certificate2(@"E:\Projects\SouthPaw\Development\public_privatekey.pfx",
                "Spectere99");
            var private_app_api = new XeroCoreApi("https://api.xero.com", new PrivateAuthenticator(cert),
                new Consumer("JSWZ2I77HEVJN3MVPAFHLXURUQO82E", "43W5TPJNATD6JE2XLSQUPPZMG4PPLG"), null,
                new DefaultMapper(), new DefaultMapper());

            var user = new ApiUser { Name = Environment.MachineName };

            var org = private_app_api.Organisation;

            
            var foundPayment =
                private_app_api.Payments.Where(string.Format("Reference == \"{0}\"", newPayment.Reference))
                    .Find()
                    .FirstOrDefault();
            Payment returnPayment = newPayment;
            if (foundPayment != null)
            {
                //Do we need to update the invoice if it is already created or do we just report it????
                newPayment.Id = foundPayment.Id;
                //returnContact = private_app_api.Contacts.Update(newContact);
            }
            else //New Contact
            {
                returnPayment = private_app_api.Payments.Create(newPayment);
            }
            return returnPayment;
        }
    }
}
