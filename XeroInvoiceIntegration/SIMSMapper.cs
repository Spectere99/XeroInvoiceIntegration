using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIMSData;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Status;
using Xero.Api.Core.Model.Types;
using Xero.Api.Infrastructure.ThirdParty.ServiceStack.Text;

namespace XeroInvoiceIntegration
{
    public class SIMSMapper
    {
        public Contact BuildContact(int customerId)
        {
            SIMSDataEntities dataEntities = new SIMSDataEntities();
            var customers = dataEntities.customers.Where(p => p.customer_id == customerId);

            customer customer = customers.FirstOrDefault();
            // Check to see if customer has a parent.  If it does, use that information to create it.
            if (customer.parent_id != null) //We have a parent.
            {
                var parentCustomers = dataEntities.customers.Where(p => p.customer_id == customer.parent_id);
                customer = parentCustomers.FirstOrDefault();

            }
            customer_address customerAddress = dataEntities.customer_address.FirstOrDefault(p => p.customer_id == customer.customer_id);
            customer_person person = dataEntities.customer_person.Where(p=>p.email_address!=null).FirstOrDefault(p => p.customer_id == customer.customer_id);

            Contact xeroContact = new Contact();
            xeroContact.Name = customer.customer_name;
            if (person != null)
            {
                xeroContact.FirstName = person.first_name.Length > 0 ? person.first_name : "NotProvide";
                xeroContact.LastName = person.last_name.Length > 0 ? person.last_name : "NotProvided";
                
                ContactPerson contactPerson = new ContactPerson();
                contactPerson.EmailAddress = person.email_address;
                contactPerson.FirstName = person.first_name;
                contactPerson.LastName = person.last_name;
                xeroContact.ContactPersons = new List<ContactPerson>();
                xeroContact.ContactPersons.Add(contactPerson);
            }

            xeroContact.AccountNumber = customer.account_number;
            xeroContact.EmailAddress = person.email_address;
            if (person.phone_1 != null)
            {
                xeroContact.Phones = new List<Phone>();
                xeroContact.Phones.Add(new Phone()
                {
                    PhoneAreaCode = person.phone_1.Substring(0, 3),
                    PhoneNumber = person.phone_1.Substring(3)
                });
            }
            if (customerAddress != null && customerAddress.address_1 != null)
            {
                xeroContact.Addresses = new List<Address>();
                Address newAddress = new Address
                {
                    AddressLine1 = customerAddress.address_1,
                    AddressLine2 = customerAddress.address_2,
                    City = customerAddress.city,
                    PostalCode = customerAddress.zip
                };
                xeroContact.Addresses.Add(newAddress);
            }

            return xeroContact;
        }

        public Invoice BuildInvoice(order theOrder, DateTime invoiceDate, string referenceNumber, Contact contact)
        {
            Invoice xeroInvoice = new Invoice();
            
            xeroInvoice.Contact = contact;
            xeroInvoice.Date = invoiceDate;
            xeroInvoice.DueDate = CalculateInvoiceDueDate(contact);
            // Invoice Number  Xero should default based on Account Settings.
            //xeroInvoice.Number = theOrder.order_number;
            
            xeroInvoice.Reference = referenceNumber;
            xeroInvoice.Status = InvoiceStatus.Submitted;
            xeroInvoice.AmountDue = decimal.Parse(theOrder.total);
            xeroInvoice.Type = InvoiceType.AccountsReceivable;
            xeroInvoice.LineItems = BuildInvoiceLineItems(theOrder);

            return xeroInvoice;
        }

        public List<LineItem> BuildInvoiceLineItems(order theOrder)
        {
            SIMSDataEntities dataEntities = new SIMSDataEntities();

            List<LineItem> lineItems = new List<LineItem>();

            IEnumerable<order_detail> orderDetails = dataEntities.order_detail.Where(o => o.order_id == theOrder.order_id);
            //TODO:  Add check for a valid details.  If quantity or price is null, then it is an invalid line item and an exception needs to be thrown and recorded.
            foreach (var detail in orderDetails)
            {
                var priceListId = detail.pricelist_id ?? default(int);
                var pricelistItems =
                    dataEntities.pricelists.FirstOrDefault(p => p.pricelist_id == priceListId);
                var itemCodeXRefs = dataEntities.item_code_xref;

                var xeroInvoiceItem = new LineItem();
                xeroInvoiceItem.AccountCode = ConfigurationManager.AppSettings["SalesAccountNumber"];  //"400";  //Check on this.  Is this correct??
                //Description is the following:
                // NOTE for VENDOR(from lookups = 'gmtpr' then "Customer Provided'
                // pricelist_description + Manufacturer + style# + Color + Size & Qty
                xeroInvoiceItem.Description = (pricelistItems != null)
                    ? pricelistItems.pricelist_description
                    : "General Item";
                //xeroInvoiceItem.ItemCode = pricelistItems.pricelist_code;
                xeroInvoiceItem.Quantity = detail.item_quantity;
                xeroInvoiceItem.LineAmount = decimal.Parse(detail.item_price_ext);
                xeroInvoiceItem.UnitAmount = decimal.Parse(detail.item_price_each);

                lineItems.Add(xeroInvoiceItem);
            }

            return lineItems;
        }
        public Payment BuildPayment(order_payments payment, Invoice xeroInvoice)
        {
            Payment xeroPayment = new Payment();

            xeroPayment.Invoice = xeroInvoice;
            xeroPayment.Date = DateTime.Parse(payment.payment_date.ToString());
            xeroPayment.Amount = Decimal.Parse(payment.payment_amount);
            xeroPayment.Account.Code = payment.payment_type_code.Equals("cash") ? "091" :"090";
            xeroPayment.Reference = payment.check_number;
            
            return xeroPayment;
        }


        private DateTime CalculateInvoiceDueDate(Contact contact)
        {
            DateTime dueDate = DateTime.Now.AddDays(5);
            if (contact.PaymentTerms != null && contact.PaymentTerms.Sales != null)
            {
                switch (contact.PaymentTerms.Sales.TermType)
                {
                    case PaymentTermType.AfterBillDate: // days after the invoice date.
                        {
                            dueDate = DateTime.Now.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.AfterInvoiceMonth: // day of the following month
                        {
                            DateTime today = DateTime.Today;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month,
                                DateTime.DaysInMonth(today.Year, today.Month));
                            dueDate = endOfMonth.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.CurrentMonth: // day of the current month
                        {
                            DateTime today = DateTime.Today;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month, contact.PaymentTerms.Sales.Day);

                            dueDate = endOfMonth;
                            break;
                        }
                    case PaymentTermType.DaysAfterBillMonth: //days after the end of the invoice month
                        {
                            DateTime today = DateTime.Today;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month,
                                DateTime.DaysInMonth(today.Year, today.Month));
                            dueDate = endOfMonth.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.FollowingMonth: // Day of the following month
                        {
                            DateTime today = DateTime.Today;
                            dueDate = new DateTime(today.Year, today.Month + 1, contact.PaymentTerms.Sales.Day);
                            break;
                        }
                }
            }
            return dueDate;
        }

    }
}
