using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Xero.Api.Core;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Status;
using Xero.Api.Core.Request;
using Xero.Api.Example.Applications.Private;
using Xero.Api.Example.Applications.Public;
using Xero.Api.Example.TokenStores;
using Xero.Api.Infrastructure.OAuth;
using Xero.Api.Infrastructure.ThirdParty.ServiceStack.Text;
using Xero.Api.Serialization;

namespace XeroInvoiceIntegration
{
    class XeroIntegration
    {
        private static log4net.ILog _log = null;
        // Private Application Sample
        private X509Certificate2 _cert = null;
        private XeroCoreApi _private_app_api = null;
        private ApiUser _user = null;
        private Organisation _org = null;

        private List<Contact> _contacts = null;
        private List<Invoice> _invoices = null;
        private List<Payment> _payments = null;

        public XeroIntegration(bool transmit)
        {
            //setup logger
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            var endPoint = ConfigurationManager.AppSettings["EndpointURL"];
            var certPath = ConfigurationManager.AppSettings["APICertFilePath"];
            var certPwd = ConfigurationManager.AppSettings["APICertPwd"];
            var consumerKey = ConfigurationManager.AppSettings["ConsumerKey"];
            var consumerSecret = ConfigurationManager.AppSettings["ConsumerSecret"];

            _cert = new X509Certificate2(certPath, certPwd);
            _private_app_api = new XeroCoreApi(endPoint, new PrivateAuthenticator(_cert),
                new Consumer(consumerKey, consumerSecret), null,
                new DefaultMapper(), new DefaultMapper());

            _user = new ApiUser {Name = Environment.MachineName};
            _org = _private_app_api.Organisation;

            _log.Info(string.Format("Connecting to Organization: {0}", _org.Name));
            if (transmit)
            {
                Console.WriteLine(string.Format("Pulling Contacts..."));
                _log.Info("Pulling Contacts...");
                int contactCount = _private_app_api.Contacts.Find().Count();
                int contactPage = 1;
                _contacts = new List<Contact>();
                while (contactCount == 100)
                {
                    try
                    {
                        var returnList =
                            _private_app_api.Contacts.Where("IsCustomer=true").Page(contactPage).Find().ToList();
                        //var returnList = _private_app_api.Contacts.Page(contactPage).Find().ToList();
                        _contacts.AddRange(returnList);
                        contactCount = returnList.Count;
                        Console.WriteLine(string.Format("Customer Count: {0}", _contacts.Count));
                        contactPage++;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(61000);
                        var returnList =
                            _private_app_api.Contacts.Where("IsCustomer=true").Page(contactPage).Find().ToList();
                        //var returnList = _private_app_api.Contacts.Page(contactPage).Find().ToList();
                        _contacts.AddRange(returnList);
                        contactCount = returnList.Count;
                        Console.WriteLine(string.Format("Customer Count: {0}", _contacts.Count));
                        contactPage++;
                    }
                }

                _log.Info(string.Format("Total Customers Pulled: {0}", _contacts.Count));
                //Console.WriteLine("Cooling Off");
                //Thread.Sleep(61000);
                Console.WriteLine(string.Format("Pulling Invoices..."));
                _log.Info("Pulling Invoices...");
                int invoiceCount = _private_app_api.Invoices.Find().Count();
                int invoicePage = 1;
                _invoices = new List<Invoice>();
                while (invoiceCount == 100)
                {
                    try
                    {
                        var returnList = _private_app_api.Invoices.Where("Status<>\"PAID\" AND Status<>\"VOIDED\" AND Status<>\"DELETED\" AND TYPE==\"ACCREC\"").Page(invoicePage).Find().ToList();
                        //var returnList = _private_app_api.Invoices.Page(invoicePage).Find().ToList();
                        _invoices.AddRange(returnList);
                        invoiceCount = returnList.Count;
                        Console.WriteLine(string.Format("Invoice Count: {0}", _invoices.Count));
                        invoicePage++;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(61000);
                        var returnList = _private_app_api.Invoices.Where("Status<>\"PAID\" AND Status<>\"VOIDED\" AND Status<>\"DELETED\" AND TYPE==\"ACCREC\"").Page(invoicePage).Find().ToList();
                        //var returnList = _private_app_api.Invoices.Page(invoicePage).Find().ToList();
                        _invoices.AddRange(returnList);
                        invoiceCount = returnList.Count;
                        Console.WriteLine(string.Format("Invoice Count: {0}", _invoices.Count));
                        invoicePage++;
                    }
                }

                _log.Info(string.Format("Total Invoices Pulled: {0}", _invoices.Count));
                Console.WriteLine("Cooling Off");
                Thread.Sleep(61000);
                _payments = _private_app_api.Payments.Find().ToList();

            }
        }

        public Prepayment FindPrepaymentById(string prepaymentId)
        {
            return _private_app_api.Prepayments.Find(prepaymentId);
        }

        public Invoice FindInvoiceDirect(string refNumber)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Reference==");
            sb.Append("\"");
            sb.Append(refNumber);
            sb.Append("\"");
            //sb.Append("AND Status<>");
            //sb.Append("\"");
            //sb.Append("PAID");
            //sb.Append("\"");
            string searchString = sb.ToString();

            var returnList = _private_app_api.Invoices.Where(searchString).Find().ToList();
            Invoice returnInvoice = null;
            if (returnList.Count > 0)
            {
                returnInvoice = returnList.SingleOrDefault();
            }
            return returnInvoice;
        }

        public Invoice FindInvoice(string refNumber)
        {
            var foundInvoice = _invoices.SingleOrDefault(p => p.Reference == refNumber);

            return foundInvoice;

        }
        public Contact CreateContact(Contact newContact, bool transmit)
        {
            var foundContact = _contacts.FirstOrDefault(p=>p.Name == newContact.Name);
            
            Contact returnContact = newContact;
            
            if (foundContact != null)
            {
                //Update Contact
                newContact.Id = foundContact.Id;
                _log.InfoFormat("Updating Contact: {0}:{1}", newContact.Name, newContact.Id);
                _log.DebugFormat("--Contact XML--");
                _log.Debug(Utilities.FormatXML(newContact.ToXml()));
                returnContact = transmit ? _private_app_api.Contacts.Update(newContact) : newContact;
            }
            else //New Contact
            {
                
                _log.InfoFormat("Creating Contact: {0}:{1}", newContact.Name, newContact.Id);
                _log.DebugFormat("--Contact XML--");
                _log.Debug(Utilities.FormatXML(newContact.ToXml()));
                returnContact = transmit ? _private_app_api.Contacts.Create(newContact) : newContact;
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

        public Invoice CreateInvoice(Invoice newInvoice, bool transmit)
        {
            var foundInvoice = _invoices.FirstOrDefault(p => p.Reference == newInvoice.Reference);
                
            Invoice returnInvoice = newInvoice;
            if (foundInvoice != null)
            {
                //Do we need to update the invoice if it is already created or do we just report it????
                newInvoice.Id = foundInvoice.Id;
                _log.InfoFormat("Invoice Exists: {0}:{1}", foundInvoice.Reference, foundInvoice.Id);
                //_log.DebugFormat("--Invoice XML--");
                //_log.Debug(Utilities.FormatXML(foundInvoice.ToXml()));
                //returnContact = private_app_api.Contacts.Update(newContact);
            }
            else //New Contact
            {
                _log.InfoFormat("Creating Invoice: {0}:{1}", newInvoice.Reference, newInvoice.Id);
                _log.DebugFormat("--Invoice XML--");
                _log.Debug(Utilities.FormatXML(newInvoice.ToXml()));
                returnInvoice = transmit ? _private_app_api.Invoices.Create(newInvoice) : newInvoice;
            }
            return returnInvoice;
        }

        public Invoice UpdateInvoice(Invoice newInvoice, bool transmit)
        {
            //var foundInvoice = _invoices.FirstOrDefault(p => p.Reference == newInvoice.Reference);

            _log.InfoFormat("Adjusting Invoice for Salestax: {0}:{1}", newInvoice.Reference, newInvoice.Id);
            _log.DebugFormat("--Invoice XML--");
            _log.Debug(Utilities.FormatXML(newInvoice.ToXml()));
            var returnInvoice = _private_app_api.Invoices.Update(newInvoice);
            
           
            return returnInvoice;
        }

        public void DeleteInvoice(Invoice delInvoice, bool transmit)
        {
            _invoices.Remove(delInvoice);
            delInvoice.Status = InvoiceStatus.Deleted;
            _log.InfoFormat("Removing Invoice for Salestax: {0}:{1}", delInvoice.Reference, delInvoice.Id);
            _log.DebugFormat("--Invoice XML--");
            _log.Debug(Utilities.FormatXML(delInvoice.ToXml()));
            _private_app_api.Invoices.Update(delInvoice);
        }

        public Payment CreatePayment(Payment newPayment, bool transmit)
        {
            // Check on prepayments.
            // Checking payment's invoice number against
            var foundPayment = _payments.FirstOrDefault(p => p.Reference == newPayment.Reference);
                
            Payment returnPayment = newPayment;
            if (foundPayment != null)
            {
                //Do we need to update the invoice if it is already created or do we just report it????
                newPayment.Id = foundPayment.Id;
                _log.InfoFormat("Payment Exists: {0}:{1}", newPayment.Reference, newPayment.Id);
                _log.DebugFormat("--Payment XML--");
                _log.Debug(Utilities.FormatXML(newPayment.ToXml()));
            }
            else //New Contact
            {
                _log.InfoFormat("Creating Payment: {0}:{1}", newPayment.Reference, newPayment.Id);
                _log.DebugFormat("--Payment XML--");
                _log.Debug(Utilities.FormatXML(newPayment.ToXml()));
                
                returnPayment = transmit? _private_app_api.Payments.Create(newPayment) : newPayment;
            }
            return returnPayment;
        }
    }
}
