﻿//------------------------------------------------------------------------------
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
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    
    public partial class SIMSDataEntities : DbContext
    {
        public SIMSDataEntities()
            : base("name=SIMSDataEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<customer> customers { get; set; }
        public virtual DbSet<customer_address> customer_address { get; set; }
        public virtual DbSet<item_code_xref> item_code_xref { get; set; }
        public virtual DbSet<order_detail> order_detail { get; set; }
        public virtual DbSet<order_payments> order_payments { get; set; }
        public virtual DbSet<order_status_history> order_status_history { get; set; }
        public virtual DbSet<order> orders { get; set; }
        public virtual DbSet<pricelist> pricelists { get; set; }
        public virtual DbSet<customer_person> customer_person { get; set; }
        public virtual DbSet<user> users { get; set; }
        public virtual DbSet<order_fees> order_fees { get; set; }
        public virtual DbSet<invoice_interface_control> invoice_interface_control { get; set; }
        public virtual DbSet<payment_interface_control> payment_interface_control { get; set; }
    }
}
