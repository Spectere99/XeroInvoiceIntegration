//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace SIMSData
{
    using System;
    using System.Collections.Generic;
    
    public partial class invoice_interface_control
    {
        public int invoice_control_id { get; set; }
        public string xero_invoice_id { get; set; }
        public int order_id { get; set; }
        public string order_number { get; set; }
        public System.DateTime invoiced_date { get; set; }
    }
}