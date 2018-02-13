using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Runtime.Remoting.Messaging;
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
            customer_person person = dataEntities.customer_person.Where(p => p.customer_id == customer.customer_id).Where(p => p.person_type == "prime").FirstOrDefault(p => p.email_address != null);

            Contact xeroContact = new Contact();
            var fixCustomerName = customer.customer_name.Replace("(Parent)", "")
                .Replace("(parent)", "")
                .Replace("(P)", "");
            xeroContact.Name = fixCustomerName.Trim();
            if (person != null)
            {
                xeroContact.FirstName = person.first_name ?? "NotProvided";
                xeroContact.LastName = person.last_name ?? "NotProvided";
                
                ContactPerson contactPerson = new ContactPerson();
                contactPerson.EmailAddress = person.email_address;
                contactPerson.FirstName = person.first_name;
                contactPerson.LastName = person.last_name;
                xeroContact.ContactPersons = new List<ContactPerson>();
                xeroContact.ContactPersons.Add(contactPerson);
            }
            

            xeroContact.AccountNumber = customer.account_number;
            if (person != null)
            {
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

        public Invoice BuildInvoice(order theOrder, DateTime invoiceDate, string referenceNumber, Contact contact, string invoiceStatus)
        {
            Invoice xeroInvoice = new Invoice();
            
            xeroInvoice.Contact = contact;
            xeroInvoice.Date = invoiceDate;
            xeroInvoice.DueDate = CalculateInvoiceDueDate(invoiceDate, contact);
            
            //xeroInvoice.LineAmountTypes = LineAmountType.NoTax;
            // Invoice Number  Xero should default based on Account Settings.
            //xeroInvoice.Number = theOrder.order_number;
            
            xeroInvoice.Reference = referenceNumber;
            if (invoiceStatus != string.Empty)
            {
                switch (invoiceStatus)
                {
                    case "Draft":
                    {
                        xeroInvoice.Status = InvoiceStatus.Draft;
                        break;
                    }
                    case "Submitted":
                    {
                        xeroInvoice.Status = InvoiceStatus.Submitted;
                        break;
                    }
                    default:
                    {
                        xeroInvoice.Status = InvoiceStatus.Draft;
                        break;
                    }
                }
            }
            //xeroInvoice.TotalTax = decimal.Parse(theOrder.tax_amount);
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
            string purchaseOrder = theOrder.purchase_order ?? string.Empty;
            //if (theOrder.purchase_order != null && (theOrder.purchase_order != null || theOrder.purchase_order.Trim().Length > 0))
            if (purchaseOrder.Trim().Length > 0)
            {
                LineItem poLine = new LineItem();
                poLine.AccountCode = ConfigurationManager.AppSettings["SalesAccountNumber"];
                poLine.Description = theOrder.purchase_order;
                poLine.ItemCode = "998";
                poLine.LineAmount = 0;
                poLine.TaxType = "NONE";
                lineItems.Add(poLine);
            }
            foreach (var detail in orderDetails)
            {
                var priceListId = detail.pricelist_id ?? default(int);
                var pricelistItems =
                    dataEntities.pricelists.FirstOrDefault(p => p.pricelist_id == priceListId);
               
                var xeroInvoiceItem = new LineItem();
                xeroInvoiceItem.AccountCode = ConfigurationManager.AppSettings["SalesAccountNumber"];  //"400";  //Check on this.  Is this correct??
                // Check and see if item is Customer Provided (gmtpr)
                // NOTE for VENDOR(from lookups = 'gmtpr' then "Customer Provided'
                if (!detail.vendor.Equals("gmtpr"))
                {
                    var detailText = GetQuantityDetailText(detail);

                    var priceListText = detail.pricelist_id != null ? detail.pricelist_id.ToString() : "";
                    var itemCodeXRef = dataEntities.item_code_xref.FirstOrDefault(p => p.source_item_code == priceListText);
                    //Description is the following:
                    // pricelist_description + Manufacturer + style# + Color + Size & Qty
                    var lineDescription = string.Format("{0} {1} {2} {3} {4}",
                        (pricelistItems != null) ? pricelistItems.pricelist_description : "Non Inventory Item",
                        detail.manufacturer, detail.product_code, detail.color_code, detailText);

                    xeroInvoiceItem.Description = lineDescription;
                    var xeroItemCode = (itemCodeXRef != null) ? itemCodeXRef.target_item_code : "999";
                    if (itemCodeXRef.target_item_code == "001")
                    {
                        if (detail.C2xl_qty != null) xeroItemCode = "002";
                        if (detail.C3xl_qty != null) xeroItemCode = "003";
                        if (detail.C4xl_qty != null) xeroItemCode = "004";
                        if (detail.C5xl_qty != null) xeroItemCode = "005";
                    }
                    xeroInvoiceItem.ItemCode = xeroItemCode;
                    //RWF - 2/12/2018 - Changed OUTPUT to TAX002 for new owners of Southpaw.
                    xeroInvoiceItem.TaxType = detail.taxable_ind == "Y" ? "TAX002": "NONE";
                }
                else
                {
                    var detailText = GetQuantityDetailText(detail);
                    List<string> orderScreenTypes = new List<string>(){"rescr", "scrn","screm"};
                    //var itemCodeXRef = dataEntities.item_code_xref.FirstOrDefault(p=>p.source_item_code.Equals(detail.pricelist_id.ToString()));
                    xeroInvoiceItem.Description = String.Format("Customer Provided for {0} {1} {2} {3} {4}",
                        orderScreenTypes.Contains(theOrder.order_type) ? "Screen" : "Embroidery",
                        detail.manufacturer, detail.product_code, detail.color_code, detailText);
                    xeroInvoiceItem.ItemCode = String.Format("{0}", orderScreenTypes.Contains(theOrder.order_type) ? "Print Only" : "emb");
                    xeroInvoiceItem.TaxType = "NONE";
                }
                xeroInvoiceItem.Quantity = detail.item_quantity;
                xeroInvoiceItem.LineAmount = detail.item_price_ext == null ? 0 : decimal.Parse(detail.item_price_ext);
                xeroInvoiceItem.UnitAmount = detail.item_price_each == null ? 0 : decimal.Parse(detail.item_price_each);
                
                lineItems.Add(xeroInvoiceItem);
            }
            // Check for setup fees and charges from the order_fees table in SIMS
            var feeItems = dataEntities.order_fees.Where(p => p.order_id == theOrder.order_id);
            foreach (order_fees feeItem in feeItems)
            {
                var xeroInvoiceItem = new LineItem();
                var priceListIdString = feeItem.pricelist_id != null? feeItem.pricelist_id.ToString() : "";
                var itemCodeXRef = dataEntities.item_code_xref.FirstOrDefault(p => p.source_item_code == priceListIdString);
                
                if (itemCodeXRef != null)
                {
                    xeroInvoiceItem.ItemCode = itemCodeXRef.target_item_code;
                    xeroInvoiceItem.AccountCode = itemCodeXRef.source_item_code == "10737"
                        ? ConfigurationManager.AppSettings["ShippingAccountNumber"]
                        : ConfigurationManager.AppSettings["SalesAccountNumber"];
                }
                else
                {
                    xeroInvoiceItem.ItemCode = "999";
                    xeroInvoiceItem.AccountCode = ConfigurationManager.AppSettings["SalesAccountNumber"];
                }
                xeroInvoiceItem.TaxType = "NONE";
                decimal feeQty = feeItem.fee_quantity != null ? decimal.Parse(feeItem.fee_quantity.ToString()) : 0;
                decimal feePriceEach = feeItem.fee_price_each != null ? decimal.Parse(feeItem.fee_price_each) : 0;

                // *** Fix for Ticket 44:  Waived Charges with value of '0' is causing invoice to not generate'
                //if (feeQty == 0)
                //{
                //    throw new Exception("Fee Quantity not provided.");
                //}
                //if (feePriceEach == 0)
                //{
                //    throw new Exception("Fee Price Each not provided");
                //}
                // *** END OF FIX for TICKET 44
                xeroInvoiceItem.LineAmount = feeItem.fee_price_ext != null? decimal.Parse(feeItem.fee_price_ext) : feePriceEach * feeItem.fee_quantity;
                xeroInvoiceItem.Quantity = feeItem.fee_quantity;
                xeroInvoiceItem.UnitAmount = feePriceEach;

                lineItems.Add(xeroInvoiceItem);
            }


            return lineItems;
        }

        public LineItem BuildSalesTaxAdjustmentLineItem(decimal? difference)
        {
            var xeroInvoiceItem = new LineItem();
            xeroInvoiceItem.AccountCode = "220";
            xeroInvoiceItem.ItemCode = "Tax";
            xeroInvoiceItem.Quantity = 1;
            xeroInvoiceItem.UnitAmount = -1 * difference;
            xeroInvoiceItem.TaxType = "NONE";

            return xeroInvoiceItem;
        }

        public Payment BuildPayment(order_payments payment, Invoice xeroInvoice)
        {
            var cashAcct = ConfigurationManager.AppSettings["CashAccountNumber"];
            var checkingAcct = ConfigurationManager.AppSettings["CheckingAccountNumber"];
            Payment xeroPayment = new Payment();

            xeroPayment.Invoice = xeroInvoice;
            xeroPayment.Date = DateTime.Parse(payment.payment_date.ToString());
            xeroPayment.Amount = Decimal.Parse(payment.payment_amount);
            xeroPayment.Account = new Account();
            
            xeroPayment.Account.Code = payment.payment_type_code.Equals("cash") ? cashAcct : checkingAcct;
            xeroPayment.Reference = payment.payment_type_code + " " + payment.check_number;
            xeroPayment.Status = PaymentStatus.Authorised;
            return xeroPayment;
        }


        private DateTime CalculateInvoiceDueDate(DateTime completeDate, Contact contact)
        {
            DateTime dueDate = completeDate.AddDays(5);
            if (contact.PaymentTerms != null && contact.PaymentTerms.Sales != null)
            {
                switch (contact.PaymentTerms.Sales.TermType)
                {
                    case PaymentTermType.AfterBillDate: // days after the invoice date.
                        {
                            dueDate = completeDate.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.AfterInvoiceMonth: // day of the following month
                        {
                            DateTime today = completeDate;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month,
                                DateTime.DaysInMonth(today.Year, today.Month));
                            dueDate = endOfMonth.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.CurrentMonth: // day of the current month
                        {
                            DateTime today = completeDate;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month, contact.PaymentTerms.Sales.Day);

                            dueDate = endOfMonth;
                            break;
                        }
                    case PaymentTermType.DaysAfterBillMonth: //days after the end of the invoice month
                        {
                            DateTime today = completeDate;
                            DateTime endOfMonth = new DateTime(today.Year, today.Month,
                                DateTime.DaysInMonth(today.Year, today.Month));
                            dueDate = endOfMonth.AddDays(contact.PaymentTerms.Sales.Day);
                            break;
                        }
                    case PaymentTermType.FollowingMonth: // Day of the following month
                        {
                            DateTime today = completeDate;
                            dueDate = new DateTime(today.Year, today.Month + 1, contact.PaymentTerms.Sales.Day);
                            break;
                        }
                }
            }
            return dueDate;
        }

        private string GetQuantityDetailText(order_detail detail)
        {
            //Need to look at th detail record and see if the quatity fields are populated.  If so, then we need to build a string to put in the item description.
            StringBuilder sizeQuantityBuilder = new StringBuilder();

            if (detail.xsmall_qty != null){sizeQuantityBuilder.Append(String.Format(" {0}({1}) ", "XS", detail.xsmall_qty));}
            if (detail.small_qty != null) { sizeQuantityBuilder.Append(String.Format(" {0}({1}) ", "S", detail.small_qty)); }
            if (detail.med_qty != null){sizeQuantityBuilder.Append(String.Format(" {0}({1}) ", "M", detail.med_qty));}
            if (detail.large_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "L", detail.large_qty));}
            if (detail.xl_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "XL", detail.xl_qty));}
            if (detail.C2xl_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "2XL", detail.C2xl_qty));}
            if (detail.C3xl_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "3XL", detail.C3xl_qty));}
            if (detail.C4xl_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "4XL", detail.C4xl_qty));}
            if (detail.C5xl_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", "5XL", detail.C5xl_qty));}
            if (detail.other1_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", detail.other1_type, detail.other1_qty));}
            if (detail.other2_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", detail.other2_type, detail.other2_qty));}
            if (detail.other3_qty != null){sizeQuantityBuilder.Append(String.Format("{0}({1}) ", detail.other3_type, detail.other3_qty));}               

            return sizeQuantityBuilder.ToString();
        }

        private long? GetQuantityTotals(order_detail detail)
        {
            long? qtyCount = 0;

            qtyCount = qtyCount + (detail.xsmall_qty ?? 0);
            qtyCount = qtyCount + (detail.small_qty ?? 0);
            qtyCount = qtyCount + (detail.med_qty ?? 0);
            qtyCount = qtyCount + (detail.large_qty ?? 0);
            qtyCount = qtyCount + (detail.xl_qty ?? 0);
            qtyCount = qtyCount + (detail.C2xl_qty ?? 0);
            qtyCount = qtyCount + (detail.C3xl_qty ?? 0);
            qtyCount = qtyCount + (detail.C4xl_qty ?? 0);
            qtyCount = qtyCount + (detail.C5xl_qty ?? 0);
            qtyCount = qtyCount + (detail.other1_qty ?? 0);
            qtyCount = qtyCount + (detail.other2_qty ?? 0);
            qtyCount = qtyCount + (detail.other3_qty ?? 0);

            return qtyCount;
        }
    }
}
