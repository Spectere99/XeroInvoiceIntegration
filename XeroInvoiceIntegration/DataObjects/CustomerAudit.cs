namespace XeroInvoiceIntegration.DataObjects
{
    public class CustomerAudit
    {
        public int CustomerID { get; set; }
        public string XeroCustomerID { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string ContactName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public string Action { get; set; }



    }
}
