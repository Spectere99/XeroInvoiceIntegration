using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIMSData;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Types;

namespace XeroInvoiceIntegration
{
    public class SIMSMapper
    {
        public Contact BuildContact(int customerId)
        {
            SIMSDataEntities dataEntities = new SIMSDataEntities();
            var customers = dataEntities.customers.Where(p => p.customer_id == customerId);

            customer customer = customers.FirstOrDefault();
            customer_address customerAddress = dataEntities.customer_address.FirstOrDefault(p => p.customer_id == customer.customer_id);
            customer_person person = dataEntities.customer_person.Where(p=>p.person_type=="prime").FirstOrDefault(p => p.customer_id == customer.customer_id);

            Contact xeroContact = new Contact();

            xeroContact.Name = customer.customer_name;
            if (person != null)
            {
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
                xeroContact.Phones.Add(new Phone() { PhoneNumber = person.phone_1 });
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
            // xeroInvoice.ExpectedPaymentDate = DateTime.Now;  Xero Should default based on contact preference.
            // Invoice Number  Xero should default based on Account Settings.
            //xeroInvoice.Number = theOrder.order_number;
            xeroInvoice.Reference = referenceNumber;
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

    }
}
