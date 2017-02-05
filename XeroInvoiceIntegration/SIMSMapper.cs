using System;
using System.Collections.Generic;
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
            customer_person person = dataEntities.customer_person.FirstOrDefault(p => p.customer_id == customer.customer_id);

            Contact xeroContact = new Contact();

            xeroContact.Name = customer.customer_name;
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
        }

        public Invoice BuildInvoice(order theOrder, Contact contact)
        {
            Invoice xeroInvoice = new Invoice();
            xeroInvoice.Contact = contact;
            xeroInvoice.Date = theOrder.act_complete_date;
            xeroInvoice.ExpectedPaymentDate = DateTime.Now.AddDays(30);
            xeroInvoice.Number = theOrder.order_number;
            xeroInvoice.Reference = theOrder.order_number;

            return xeroInvoice;
        }

        public List<LineItem> BuildInvoiceLineItems(order theOrder)
        {
            List<LineItem> lineItems = new List<LineItem>();


            return lineItems;
        }
        public Payment BuildPayment(int orderId, Contact contact)
        {
            Payment xeroPayment = new Payment();

            return xeroPayment;
        }

    }
}
