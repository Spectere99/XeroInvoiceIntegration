using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using log4net;
using Xero.Api.Core;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Status;
using Xero.Api.Example.Applications.Private;
using Xero.Api.Infrastructure.Exceptions;
using Xero.Api.Infrastructure.OAuth;
using Xero.Api.Infrastructure.ThirdParty.ServiceStack.Text;
using Xero.Api.Serialization;
using Organisation = Xero.Api.Core.Model.Organisation;

namespace XeroInvoiceIntegration
{
    class XeroIntegration
    {
        private static ILog _log;
        // Private Application Sample
        private readonly XeroCoreApi _privateAppApi;
        private Organisation _org;

        private readonly List<Contact> _contacts;
        private readonly List<Invoice> _invoices;
        private readonly List<Payment> _payments;

        public XeroIntegration(bool transmit, bool dailyProcess)
        {
            //setup logger, 
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            var endPoint = ConfigurationManager.AppSettings["EndpointURL"];
            var certPath = ConfigurationManager.AppSettings["APICertFilePath"];
            var certPwd = ConfigurationManager.AppSettings["APICertPwd"];
            var consumerKey = ConfigurationManager.AppSettings["ConsumerKey"];
            var consumerSecret = ConfigurationManager.AppSettings["ConsumerSecret"];

            try
            {

                var cert = new X509Certificate2(certPath, certPwd);
                _privateAppApi = new XeroCoreApi(endPoint, new PrivateAuthenticator(cert),
                    new Consumer(consumerKey, consumerSecret), null,
                    new DefaultMapper(), new DefaultMapper());

                new ApiUser {Name = Environment.MachineName};
                _org = _privateAppApi.Organisation;

                _log.Info(string.Format("Connecting to Organization: {0}", _org.Name));
                //if (transmit)
                //{
                Console.WriteLine("Pulling Contacts...");
                _log.Info("Pulling Contacts...");
                int contactCount = _privateAppApi.Contacts.Find().Count();
                int contactPage = 1;
                List<Contact> contactReturnList;
                _contacts = new List<Contact>();
                while (contactCount == 100)
                {
                    try
                    {
                        contactReturnList =
                            _privateAppApi.Contacts.Where("IsCustomer=true").Page(contactPage).Find().ToList();
                        //var returnList = _private_app_api.Contacts.Page(contactPage).Find().ToList();
                        _contacts.AddRange(contactReturnList);
                        contactCount = contactReturnList.Count;
                        Console.WriteLine("Customer Count: {0}", _contacts.Count);
                        contactPage++;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(61000);
                        var returnList =
                            _privateAppApi.Contacts.Where("IsCustomer=true").Page(contactPage).Find().ToList();
                        //var returnList = _private_app_api.Contacts.Page(contactPage).Find().ToList();
                        _contacts.AddRange(returnList);
                        contactCount = returnList.Count;
                        Console.WriteLine("Customer Count: {0}", _contacts.Count);
                        contactPage++;
                    }
                }

                _log.Info(string.Format("Total Customers Pulled: {0}", _contacts.Count));

                Console.WriteLine("Pulling Invoices...");
                _log.Info("Pulling Invoices...");
                int invoiceCount = _privateAppApi.Invoices.Find().Count();
                int invoicePage = 1;
                _invoices = new List<Invoice>();

                List<Invoice> returnInvoiceList;
                var invoiceQuery = dailyProcess
                    ? "Status<>\"PAID\" AND Status<>\"VOIDED\" AND Status<>\"DELETED\" AND TYPE==\"ACCREC\""
                    : "Status<>\"VOIDED\" AND Status<>\"DELETED\" AND TYPE==\"ACCREC\"";

                while (invoiceCount == 100)
                {
                    try
                    {
                        if (dailyProcess)
                        {
                            returnInvoiceList =
                                _privateAppApi.Invoices.Where(invoiceQuery)
                                    .Page(invoicePage)
                                    .Find()
                                    .ToList();
                        }
                        else
                        {
                            returnInvoiceList =
                                _privateAppApi.Invoices.Where(invoiceQuery)
                                    .ModifiedSince(DateTime.Now.AddDays(-30))
                                    .Page(invoicePage)
                                    .Find()
                                    .ToList();
                        }
                        //var returnList = _private_app_api.Invoices.Page(invoicePage).Find().ToList();
                        _invoices.AddRange(returnInvoiceList);
                        invoiceCount = returnInvoiceList.Count;
                        Console.WriteLine("Invoice Count: {0}", _invoices.Count);
                        invoicePage++;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(61000);
                        if (dailyProcess)
                        {
                            returnInvoiceList =
                                _privateAppApi.Invoices.Where(invoiceQuery)
                                    .Page(invoicePage)
                                    .Find()
                                    .ToList();
                        }
                        else
                        {
                            returnInvoiceList =
                                _privateAppApi.Invoices.Where(invoiceQuery)
                                    .ModifiedSince(DateTime.Now.AddDays(-30))
                                    .Page(invoicePage)
                                    .Find()
                                    .ToList();
                        }
                        //var returnList = _private_app_api.Invoices.Page(invoicePage).Find().ToList();
                        _invoices.AddRange(returnInvoiceList);
                        invoiceCount = returnInvoiceList.Count;
                        Console.WriteLine("Invoice Count: {0}", _invoices.Count);
                        invoicePage++;
                    }
                }

                _log.Info(string.Format("Total Invoices Pulled: {0}", _invoices.Count));
                Console.WriteLine("Cooling Off");
                Thread.Sleep(30000);
                _payments = _privateAppApi.Payments.Find().ToList();
            }
            //}
            catch (Exception ex)
            {
                _log.Error(string.Format("Erorr Text: {0}", ex.Message));
                throw new Exception("An Error occurred inside of XeroIntegration", ex);
            }

        }

        public Prepayment FindPrepaymentById(string prepaymentId)
        {
            return _privateAppApi.Prepayments.Find(prepaymentId);
        }

        public Invoice FindInvoiceDirect(string refNumber)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Reference==");
            sb.Append("\"");
            sb.Append(refNumber);
            sb.Append("\"");
            sb.Append("AND Status<>");
            sb.Append("\"");
            sb.Append("DELETED");
            sb.Append("\"");
            string searchString = sb.ToString();
            Invoice returnInvoice = null;
            try
            {
                var returnList = _privateAppApi.Invoices.Where(searchString).Find().ToList();
                
                if (returnList.Count > 0)
                {
                    returnInvoice = returnList.SingleOrDefault();
                }
            }
            catch (Exception)
            {
                Thread.Sleep(61000);
                var returnList = _privateAppApi.Invoices.Where(searchString).Find().ToList();
                
                if (returnList.Count > 0)
                {
                    returnInvoice = returnList.SingleOrDefault();
                }
            }
            
            return returnInvoice;
        }

        public Invoice FindInvoice(string refNumber)
        {
            var foundInvoice = _invoices.SingleOrDefault(p => p.Reference == refNumber);

            return foundInvoice;

        }


        public Tuple<Contact, string> CreateContact(Contact newContact, bool transmit)
        {
            var foundContact = _contacts.FirstOrDefault(p => p.Name == newContact.Name);
            var actionType = "FOUND";
            int retryCount = 0;
            Contact returnContact = newContact;
            //int idx = 0;
            bool addedContact = false;

            while (!addedContact)
            {
                try
                {
                    if (foundContact != null)
                    {
                        //Update Contact
                        newContact.Id = foundContact.Id;
                        _log.InfoFormat("Contact Found: {0}:{1}", newContact.Name, newContact.Id);
                        _log.DebugFormat("--Contact XML--");
                        _log.Debug(Utilities.FormatXML(newContact.ToXml()));
                        returnContact = newContact;
                        //returnContact = transmit ? _private_app_api.Contacts.Update(newContact) : newContact;
                        addedContact = true;
                    }
                    else //New Contact
                    {
                        _log.InfoFormat("Creating Contact: {0}:{1}", newContact.Name, newContact.Id);
                        _log.DebugFormat("--Contact XML--");
                        _log.Debug(Utilities.FormatXML(newContact.ToXml()));
                        returnContact = transmit ? _privateAppApi.Contacts.Create(newContact) : newContact;
                        actionType = "CREATED";
                        addedContact = true;
                    }

                }
                catch (ValidationException) 
                {
                    //Validation exceptions from Xero occurr because the same person is listed for multiple Customers and Xero does not like that.  
                    // If we hit this, then we will remove the person, but leave the email address and still save the Customer so the Invoice can be
                    // created in the future.
                    if (retryCount > 0)
                    {
                        addedContact = true;
                        returnContact = newContact;
                        actionType = "SKIPPED";
                    }
                    
                    newContact.ContactPersons = null;
                    newContact.FirstName = null;
                    newContact.LastName = null;
                    retryCount++;
                }
            }


            return new Tuple<Contact, string>(returnContact, actionType);
        }

        public Tuple<Invoice, string> CreateInvoice(Invoice newInvoice, bool transmit)
        {
            var foundInvoice = _invoices.FirstOrDefault(p => p.Reference == newInvoice.Reference);
            var actionType = "FOUND"; 
            Invoice returnInvoice = newInvoice;
            if (foundInvoice != null)
            {
                //Do we need to update the invoice if it is already created or do we just report it????
                newInvoice.Id = foundInvoice.Id;
                _log.InfoFormat("Invoice Exists: {0}:{1}", foundInvoice.Reference, foundInvoice.Id);
               
            }
            else //New Contact
            {
                _log.InfoFormat("Creating Invoice: {0}:{1}", newInvoice.Reference, newInvoice.Id);
                _log.DebugFormat("--Invoice XML--");
                _log.Debug(Utilities.FormatXML(newInvoice.ToXml()));
                returnInvoice = transmit ? _privateAppApi.Invoices.Create(newInvoice) : newInvoice;
                actionType = "CREATED";
            }
            return new Tuple<Invoice, string>(returnInvoice, actionType);
        }

        public Tuple<Invoice, string> UpdateInvoice(Invoice newInvoice, bool transmit)
        {
            //var foundInvoice = _invoices.FirstOrDefault(p => p.Reference == newInvoice.Reference);
            var actionType = "UPDATE";
            _log.InfoFormat("Adjusting Invoice for Salestax: {0}:{1}", newInvoice.Reference, newInvoice.Id);
            _log.DebugFormat("--Invoice XML--");
            _log.Debug(Utilities.FormatXML(newInvoice.ToXml()));
            var returnInvoice = transmit ? _privateAppApi.Invoices.Update(newInvoice) : newInvoice;
            
           
            return new Tuple<Invoice, string>(returnInvoice, actionType);
        }

        public void DeleteInvoice(Invoice delInvoice, bool transmit)
        {
            _invoices.Remove(delInvoice);
            delInvoice.Status = InvoiceStatus.Deleted;
            _log.InfoFormat("Removing Invoice for Salestax: {0}:{1}", delInvoice.Reference, delInvoice.Id);
            _log.DebugFormat("--Invoice XML--");
            _log.Debug(Utilities.FormatXML(delInvoice.ToXml()));
            if (transmit)
            {
                _privateAppApi.Invoices.Update(delInvoice);
            }
        }

        public Payment FindXeroPaymentByReference(string reference)
        {
            return _payments.FirstOrDefault(p => p.Invoice.Reference == reference);
        }
        public Tuple<Payment, string> CreatePayment(Payment newPayment, bool transmit)
        {
            // Check on prepayments.
            // Checking payment's invoice number against
            var foundPayment = _payments.FirstOrDefault(p => p.Invoice.Reference == newPayment.Invoice.Reference);
            var actionType = "FOUND"; 
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
                
                returnPayment = transmit? _privateAppApi.Payments.Create(newPayment) : newPayment;
                actionType = "CREATED";
            }
            return new Tuple<Payment, string>(returnPayment, actionType);
        }
    }
}
