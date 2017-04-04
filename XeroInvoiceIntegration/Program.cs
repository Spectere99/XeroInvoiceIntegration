﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using CsvHelper;
using SIMSData;
using Xero.Api.Core.Model;
using ValidationException = Xero.Api.Infrastructure.Exceptions.ValidationException;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using Xero.Api.Core.Model.Status;
using XeroInvoiceIntegration.DataObjects;

namespace XeroInvoiceIntegration
{
    class Program
    {

        private static ILog _log;
        private static int _transactionCount;
        private static DateTime _lastTime;
        private static TimeSpan _elapsedTimeSpan = TimeSpan.Zero;

        static void Main(string[] args)
        {
            //setup commandline Options
            var options = new Options();
            TextWriter customerAuditTextWriter;
            TextWriter invoiceAuditTextWriter;
            TextWriter paymentAuditTextWriter;
            TextWriter exceptionAuditTextWriter;

            _lastTime = DateTime.Now;
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {
                //setup logger
                _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
                DateTime startTime = DateTime.Now;
                try
                {

                    
                    //setup audit file
                    string auditLocationBase = ConfigurationManager.AppSettings["AuditFilesLocation"];

                    string auditCustomerFile = auditLocationBase +
                                               DateTime.Now.ToString("yyyyMMddhhmmss") + "_Customer.csv";
                    string auditInvoiceFile = auditLocationBase +
                                              DateTime.Now.ToString("yyyyMMddhhmmss") + "_Invoice.csv";
                    string auditPaymentFile = auditLocationBase +
                                              DateTime.Now.ToString("yyyyMMddhhmmss") + "_Payment.csv";
                    string exceptionFile =  auditLocationBase +
                                           DateTime.Now.ToString("yyyyMMddhhmmss") + "_Exceptions.csv";

                    if (!Directory.Exists(auditLocationBase))
                    {
                        Directory.CreateDirectory(auditLocationBase);
                    }

                    customerAuditTextWriter = new StreamWriter(auditCustomerFile);
                    invoiceAuditTextWriter = new StreamWriter(auditInvoiceFile);
                    paymentAuditTextWriter = new StreamWriter(auditPaymentFile);
                    exceptionAuditTextWriter = new StreamWriter(exceptionFile);

                    var customerCsv = new CsvWriter(customerAuditTextWriter);
                    var invoiceCsv = new CsvWriter(invoiceAuditTextWriter);
                    var paymentCsv = new CsvWriter(paymentAuditTextWriter);
                    var exceptionCsv = new CsvWriter(exceptionAuditTextWriter);

                    bool customerHeaderWritten = false;
                    bool invoiceHeaderWritten = false;
                    bool paymentHeaderWritten = false;
                    bool exceptionHeaderWritten = false;


                    bool dailyRun = (ConfigurationManager.AppSettings["DailyRun"] == "Y");
                    bool emailResults = (ConfigurationManager.AppSettings["EmailResults"] == "Y");
                    string[] emailTo = ConfigurationManager.AppSettings["ToEmail"].Split(',');
                    var startDateConfig = ConfigurationManager.AppSettings["StartDate"];
                    var endDateConfig = ConfigurationManager.AppSettings["EndDate"];
                    var paymentBackDate = DateTime.Parse(ConfigurationManager.AppSettings["PaymentBackDate"]);
                    string invoiceStatus = ConfigurationManager.AppSettings["InvoiceStatus"];

                    SIMSDataEntities dataEntities = new SIMSDataEntities();
                    SIMSMapper simsMapper = new SIMSMapper();

                    var now = DateTime.Now;
                    var defaultStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                    DateTime configStartDate = DateTime.Parse(startDateConfig);
                    DateTime configEndDate = DateTime.Parse(endDateConfig);

                    DateTime selectDate = defaultStart;
                    if (!dailyRun)
                    {
                        selectDate = configStartDate;
                    }
                    
                    _log.Info("Processing SIMS Data...");
                    _log.Info(string.Format(" Run Date: {0}", DateTime.Now));
                    _log.Info("***************** Parameters *********************");
                    _log.Info(string.Format("** Daily Run: {0}", dailyRun));
                    _log.Info(string.Format("** StartDate Config: {0}", startDateConfig));
                    _log.Info(string.Format("** EndDate Config: {0}", endDateConfig));
                    _log.Info(string.Format("** Configured Start Date: {0}", configStartDate));
                    _log.Info(string.Format("** Select Date: {0}", selectDate));
                    _log.Info(string.Format("** Transmit to XERO?: {0}", options.TransmitToXero));
                    _log.Info(string.Format("** Invoice Status: {0}", invoiceStatus));
                    _log.Info(string.Format("** Customer Audit File: {0}", auditCustomerFile));
                    _log.Info(string.Format("** Invoice Audit File: {0}", auditInvoiceFile));
                    _log.Info(string.Format("** Payment Audit File: {0}", auditPaymentFile));
                    _log.Info(string.Format("** Exception Audit File: {0}", exceptionFile));
                    _log.Info("***************** End Parameters *********************");
                    _log.Debug("********* Checking SIMS Order History");
                    
                    List<order_status_history> dailyOrderNumbers;

                    if (dailyRun)
                    {
                        dailyOrderNumbers =
                            dataEntities.order_status_history.Where(p => p.order_status.Equals("com"))
                                .Where(o => o.status_date >= selectDate).ToList();
                    }
                    else
                    {
                        dailyOrderNumbers =
                            dataEntities.order_status_history.Where(p => p.order_status.Equals("com"))
                                .Where(o => o.status_date >= configStartDate && o.status_date < configEndDate).ToList();
                    }
                    _log.Debug("********* Creating XeroIntegration Object");

                    XeroIntegration xeroIntegration = new XeroIntegration(options.TransmitToXero, dailyRun);
                    
                    _log.Info(string.Format("********** Processing {0} Daily Orders *************",
                        dailyOrderNumbers.Count));

                    foreach (var stat in dailyOrderNumbers)
                    {
                        CustomerAudit customerAudit = new CustomerAudit();
                        InvoiceAudit invoiceAudit = new InvoiceAudit();
                        PaymentAudit paymentAudit = new PaymentAudit();
                        try
                        {
                            var orderIdSearch = int.Parse(stat.order_id);
                            Console.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
                            _log.Info(string.Format("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date));

                            var orderHeaders = dataEntities.orders.Where(o => o.order_id == orderIdSearch).ToList();
                            foreach (var header in orderHeaders)
                            {
                                //Check on the Customer / Xero Contact
                                if (header.customer_id != null)
                                {
                                    int customerId = int.Parse(header.customer_id.ToString());

                                    var xeroContact = simsMapper.BuildContact(customerId);
                                    customerAudit = new CustomerAudit();
                                    if (!customerHeaderWritten)
                                    {
                                        customerCsv.WriteHeader(customerAudit.GetType());
                                        customerHeaderWritten = true;
                                    }

                                    if (options.TransmitToXero)
                                    {
                                        WaitCheck(2);
                                    }

                                    var contactCreateReturn = xeroIntegration.CreateContact(xeroContact, options.TransmitToXero);
                                    xeroContact = contactCreateReturn.Item1;
                                    customerAudit.Action = contactCreateReturn.Item2;

                                    customerAudit.CustomerID = customerId;
                                    customerAudit.CustomerName = xeroContact.Name;
                                    customerAudit.Address = xeroContact.Addresses != null
                                        ? xeroContact.Addresses.FirstOrDefault().AddressLine1
                                        : string.Empty;
                                    customerAudit.City = xeroContact.Addresses != null
                                        ? xeroContact.Addresses.FirstOrDefault().City
                                        : string.Empty;
                                    customerAudit.State = xeroContact.Addresses != null
                                        ? xeroContact.Addresses.FirstOrDefault().Region
                                        : string.Empty;
                                    customerAudit.Zip = xeroContact.Addresses != null
                                        ? xeroContact.Addresses.FirstOrDefault().PostalCode
                                        : string.Empty;
                                    customerAudit.ContactEmail = xeroContact.EmailAddress ?? string.Empty;
                                    if (xeroContact.ContactPersons != null)
                                    {
                                        if (xeroContact.ContactPersons.Count != 0)
                                        {
                                            customerAudit.ContactName = string.Format("{0} {1}",
                                                xeroContact.ContactPersons.FirstOrDefault().FirstName,
                                                xeroContact.ContactPersons.FirstOrDefault().LastName);
                                        }
                                    }
                                    if (xeroContact.Phones != null)
                                    {
                                        if (xeroContact.Phones != null || xeroContact.Phones.Count != 0)
                                        {
                                            customerAudit.ContactPhone = string.Format("({0}){1}",
                                                xeroContact.Phones.FirstOrDefault().PhoneAreaCode,
                                                xeroContact.Phones.FirstOrDefault().PhoneNumber);
                                        }
                                    }
                                    customerAudit.Email = xeroContact.EmailAddress;
                                    customerCsv.WriteRecord(customerAudit);

                                    string orderid = header.order_id.ToString();
                                    DateTime completeDate = DateTime.Now;
                                    var statusRecords =
                                        dataEntities.order_status_history.Where(o => o.order_id == orderid).ToList();
                                    if (statusRecords != null)
                                    {
                                        order_status_history statusHistory = statusRecords.FirstOrDefault(d => d.order_status.Equals("com"));  
                                        completeDate = DateTime.Parse(statusHistory.status_date.ToString());  
                                    }
                                    
                                    user assignedTo =
                                        dataEntities.users.FirstOrDefault(o => o.user_id == header.assigned_user_id);
                                    
                                    string referenceNumber = assignedTo.first_name.Substring(0, 1).ToUpper() +
                                                             assignedTo.last_name.Substring(0, 1).ToUpper() + " " +
                                                             header.order_number;

                                    var existingInvoice =
                                        dataEntities.invoice_interface_control.Any(p => p.order_id == header.order_id);
                                    //Build Invoice
                                    if (!existingInvoice)
                                    {
                                        var xeroInvoice = simsMapper.BuildInvoice(header, completeDate, referenceNumber,
                                            xeroContact, invoiceStatus);
                                        invoiceAudit = new InvoiceAudit();
                                        if (!invoiceHeaderWritten)
                                        {
                                            invoiceCsv.WriteHeader(invoiceAudit.GetType());
                                            invoiceHeaderWritten = true;
                                        }

                                        // Create the Invoice
                                        if (options.TransmitToXero)
                                        {
                                            WaitCheck(1);
                                        }
                                        var simsInvoiceTotal = xeroInvoice.AmountDue;

                                        Tuple<Invoice, string> invoiceCreateReturn = xeroIntegration.CreateInvoice(xeroInvoice, options.TransmitToXero);
                                        xeroInvoice = invoiceCreateReturn.Item1;

                                        invoiceAudit.Action = invoiceCreateReturn.Item2;
                                        decimal? invoiceDiff = xeroInvoice.AmountDue - simsInvoiceTotal;
                                        if (invoiceDiff != (decimal?) 0.0)
                                        {
                                            xeroIntegration.DeleteInvoice(xeroInvoice, options.TransmitToXero);
                                            xeroInvoice.LineItems.Add(
                                                simsMapper.BuildSalesTaxAdjustmentLineItem(invoiceDiff));
                                            xeroInvoice.Status = (invoiceStatus.ToUpper() == "SUBMITTED")?InvoiceStatus.Submitted:InvoiceStatus.Draft;
                                            xeroInvoice.Id = Guid.Empty;
                                            xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice,
                                                options.TransmitToXero).Item1;
                                        }
                                        if (xeroInvoice.Id != Guid.Empty)
                                        {
                                            //var x = 0;
                                            //Don't update status!!!!!  Doing so breaks SIMS!!!!!
                                            //int nextPk =
                                            //    dataEntities.order_status_history.OrderByDescending(
                                            //            p => p.order_status_history_id)
                                            //        .FirstOrDefault()
                                            //        .order_status_history_id +
                                            //    1;
                                            //// Update SIMS order_status_history table.
                                            //order_status_history orderStatus = new order_status_history();
                                            //orderStatus.order_status_history_id = nextPk;
                                            //orderStatus.order_id = header.order_id.ToString();
                                            //orderStatus.order_status = "inst";
                                            //orderStatus.status_date = DateTime.Now;
                                            //orderStatus.set_by_user_id = int.Parse(simsUserId);

                                            ////dataEntities.Database.Log = s => Debug.WriteLine(s);

                                            //dataEntities.order_status_history.Add(orderStatus);
                                            //dataEntities.SaveChanges();

                                            //Check and see if 0 balance.   //Don't update status!!!!!  Doing so breaks SIMS!!!!!
                                            //if (double.Parse(header.balance_due) == 0.0)
                                            //{
                                            //    int clsdPk = dataEntities.order_status_history.OrderByDescending(
                                            //                            p => p.order_status_history_id)
                                            //                        .FirstOrDefault()
                                            //                        .order_status_history_id +
                                            //                    1;
                                            //    order_status_history orderClosedStatus = new order_status_history();
                                            //    orderClosedStatus.order_status_history_id = clsdPk;
                                            //    orderClosedStatus.order_id = header.order_id.ToString();
                                            //    orderClosedStatus.order_status = "clos";
                                            //    orderClosedStatus.status_date = DateTime.Now;
                                            //    orderClosedStatus.set_by_user_id = int.Parse(simsUserId);

                                            //    dataEntities.order_status_history.Add(orderClosedStatus);
                                            //    dataEntities.SaveChanges();
                                            //}

                                        }

                                        //Create new record for Invoice_Control table to say we have created and sent this invoice
                                        invoice_interface_control invoiceControl = new invoice_interface_control
                                        {
                                            order_id = header.order_id,
                                            invoiced_date = DateTime.Now,
                                            order_number = header.order_number,
                                            xero_invoice_id = xeroInvoice.Id.ToString()
                                        };

                                        dataEntities.invoice_interface_control.Add(invoiceControl);
                                        dataEntities.SaveChanges();

                                        //var order = from ord in dataEntities.orders
                                        //    where ord.order_id == header.order_id
                                        //    select ord;

                                        //order updOrder = order.Single();
                                        //updOrder.xero_invoice_id = xeroInvoice.Id.ToString();

                                        //dataEntities.SaveChanges();
                                   

                                        invoiceAudit.CreateDate = xeroInvoice.Date;
                                        invoiceAudit.InvoiceAmt = xeroInvoice.AmountDue;
                                        invoiceAudit.InvoiceDueDate = xeroInvoice.DueDate;
                                        invoiceAudit.LineItemCount = xeroInvoice.LineItems.Count;
                                        invoiceAudit.OrderId = header.order_id;
                                        invoiceAudit.OrderNumber = header.order_number;
                                        invoiceAudit.ReferenceNbr = xeroInvoice.Reference;
                                        invoiceAudit.XeroInvoiceId = xeroInvoice.Id.ToString();

                                        invoiceCsv.WriteRecord(invoiceAudit);
                                    }
                                }
                            }
                        }
                        catch (ValidationException valEx)
                        {
                            var st = new StackTrace(valEx, true);
                            var frame = st.GetFrame(0);
                            var line = frame.GetFileLineNumber();
                            ExceptionAudit exceptionAudit = LogExceptionData(valEx, customerAudit, invoiceAudit,
                                paymentAudit);
                            if (!exceptionHeaderWritten)
                            {
                                exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                exceptionHeaderWritten = true;
                            }
                            exceptionCsv.WriteRecord(exceptionAudit);
                            _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line,
                                valEx.Message);
                            _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(valEx));
                        }
                        catch (Exception ex)
                        {
                            var st = new StackTrace(ex, true);
                            var frame = st.GetFrame(0);
                            var line = frame.GetFileLineNumber();
                            ExceptionAudit exceptionAudit = LogExceptionData(ex, customerAudit, invoiceAudit,
                                paymentAudit);
                            if (!exceptionHeaderWritten)
                            {
                                exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                exceptionHeaderWritten = true;
                            }
                            exceptionCsv.WriteRecord(exceptionAudit);
                            _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line,
                                ex.Message);
                            _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(ex));
                        }

                    }
                    //RWF new ADD 2/28/2017
                    //Check for payments on previous invoices.
                    //if (options.TransmitToXero)
                    //{
                        //var pastOrderPaymentsNotProcessed =
                        //    dataEntities.order_payments.Where(
                        //        p => p.xero_payment_id == null || p.xero_payment_id == emptyGuid).ToList();
                        var pastOrderPaymentsDated =
                            dataEntities.order_payments.Where(o => o.payment_date >= paymentBackDate).ToList();
                        var pastOrderPayments =
                            pastOrderPaymentsDated.Where(q => q.payment_date != DateTime.Now).ToList();
                        var pastOrderPaymentsNotNull = pastOrderPayments.Where(p => p.payment_amount != null).ToList();

                        Console.WriteLine("  **** Processing Payments for Past Invoices ****");
                        Console.WriteLine("Processing {0} Past Payments", pastOrderPaymentsNotNull.Count);
                        _log.Info("  **** Processing Payments for Past Invoices ****");
                        _log.Info(string.Format("Processing {0} Past Payments", pastOrderPaymentsNotNull.Count));

                        foreach (order_payments pastPayment in pastOrderPaymentsNotNull)
                        {
                            PaymentAudit paymentAudit = new PaymentAudit();
                            try
                            {
                                 var processedPayment =
                                        dataEntities.payment_interface_control.Any(p=>p.order_payment_id == pastPayment.order_payment_id);
                                if (!processedPayment)
                                {
                                    Console.WriteLine(" *** Processing PaymentID: {0}", pastPayment.order_payment_id);
                                    _log.Info(string.Format(" *** Processing PaymentID: {0}",
                                        pastPayment.order_payment_id));
                                    var pastOrder =
                                        dataEntities.orders.SingleOrDefault(p => p.order_id == pastPayment.order_id);
                                    string orderId = pastOrder.order_id.ToString();
                                    if (pastOrder != null)
                                    {
                                        user assignedTo =
                                            dataEntities.users.FirstOrDefault(
                                                o => o.user_id == pastOrder.assigned_user_id);

                                        string referenceNumber;
                                        if (assignedTo != null)
                                        {
                                            referenceNumber = assignedTo.first_name.Substring(0, 1).ToUpper() +
                                                              assignedTo.last_name.Substring(0, 1).ToUpper() + " " +
                                                              pastOrder.order_number;
                                        }
                                        else
                                        {
                                            paymentAudit = new PaymentAudit
                                            {
                                                OrderId = pastOrder.order_id,
                                                OrderNumber = pastOrder.order_number
                                            };

                                            ExceptionAudit exceptionAudit =
                                                LogExceptionData(new Exception("Assigned To User Not Set in SIMS"), null,
                                                    null, paymentAudit);
                                            if (!exceptionHeaderWritten)
                                            {
                                                exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                                exceptionHeaderWritten = true;
                                            }
                                            exceptionCsv.WriteRecord(exceptionAudit);
                                            continue;
                                        }
                                        //Check order to see if it is closed.
                                        _log.Info(string.Format(" *** Reference Number: {0}", referenceNumber));
                                        //var foundOrderStatus =
                                        //    dataEntities.order_status_history.Where(p => p.order_id == orderId && p.order_status == "clos"                                            );
                                        if (!dataEntities.order_status_history.Any(
                                            p => p.order_id == orderId && p.order_status == "clos"))
                                        {
                                            _log.Info(" *** Not Already Closed in SIMS ***");
                                            // Check to see if it is in invoiced status
                                            //foundOrderStatus =
                                            //    dataEntities.order_status_history.SingleOrDefault(
                                            //        p => p.order_id == orderId && p.order_status == "inst");

                                            if (!dataEntities.order_status_history.Any(
                                                p => p.order_id == orderId && p.order_status == "inst"))
                                            {
                                                _log.Info(" *** Not Already Invoiced in SIMS ***");
                                                Invoice matchInvoice = xeroIntegration.FindInvoiceDirect(referenceNumber);
                                                if (matchInvoice != null)
                                                {
                                                    _log.Info(" *** Found Matching Invoice in Xero ***");
                                                    if (matchInvoice.Status != InvoiceStatus.Paid)
                                                    {
                                                        _log.Info(" *** Not Paid ***");
                                                        bool foundMatchedPayment = false;
                                                        // Get All payments for the order.
                                                        int? ordId = int.Parse(orderId);
                                                        var allPayments =
                                                            dataEntities.order_payments.Where(
                                                                p => p.order_id == ordId).ToList();
                                                        //Check to see if the payment is a prepayment.
                                                        foreach (order_payments payment in allPayments)
                                                        {
                                                            Prepayment prePayment = null;

                                                            if (matchInvoice.Prepayments != null)
                                                            {
                                                                _log.Info(" *** Checking PrePayments ***");
                                                                foreach (
                                                                    Prepayment basePrePayment in
                                                                    matchInvoice.Prepayments)
                                                                {
                                                                    Prepayment fullPrePayment =
                                                                        xeroIntegration.FindPrepaymentById(
                                                                            basePrePayment.Id.ToString());

                                                                    if (fullPrePayment != null)
                                                                    {
                                                                        foreach (
                                                                            PrepaymentAllocation allocation in
                                                                            fullPrePayment.Allocations)
                                                                        {
                                                                            if (fullPrePayment.Date ==
                                                                                payment.payment_date &&
                                                                                allocation.Amount ==
                                                                                decimal.Parse(payment.payment_amount))
                                                                            {

                                                                                prePayment = fullPrePayment;
                                                                                break;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            if (prePayment != null) //We found a prepayment
                                                            {
                                                                _log.Info(
                                                                    string.Format(
                                                                        " *** Found Existing Prepayment in Xero {0} ***",
                                                                        prePayment.Id));
                                                                //Update the Sims payment with the pre-payment ID
                                                                Common.RecordXeroPaymentControl(pastOrder.order_number,
                                                                    payment, prePayment.Id.ToString());

                                                                foundMatchedPayment = true;
                                                            }

                                                            Payment existingPayment = null;

                                                            foreach (Payment basePayment in matchInvoice.Payments)
                                                            {
                                                                _log.Info(" *** Checking Payments ***");
                                                                if (
                                                                    basePayment.Reference.Contains(payment.check_number) &&
                                                                    basePayment.Amount ==
                                                                    decimal.Parse(payment.payment_amount) &&
                                                                    basePayment.Date == payment.payment_date)
                                                                {
                                                                    existingPayment = basePayment;
                                                                    break;
                                                                }
                                                            }

                                                            if (existingPayment != null) //We found a Payment
                                                            {
                                                                _log.Info(
                                                                    string.Format(
                                                                        " *** Found Existing Payment in Xero {0} ***",
                                                                        existingPayment.Id));
                                                                //Update the Sims payment with the pre-payment ID
                                                                Common.RecordXeroPaymentControl(pastOrder.order_number,
                                                                    payment, existingPayment.Id.ToString());

                                                                foundMatchedPayment = true;
                                                            }

                                                            if (!foundMatchedPayment &&
                                                                matchInvoice.Status == InvoiceStatus.Authorised)
                                                            {
                                                                //Apply the payment.
                                                                _log.Info(
                                                                    string.Format(" *** Appyling SIMS Payment {0} ***",
                                                                        pastPayment.order_payment_id));
                                                                Payment xeroPayment =
                                                                    simsMapper.BuildPayment(pastPayment,
                                                                        matchInvoice);
                                                                paymentAudit = new PaymentAudit();
                                                                if (!paymentHeaderWritten)
                                                                {
                                                                    paymentCsv.WriteHeader(paymentAudit.GetType());
                                                                    paymentHeaderWritten = true;
                                                                }


                                                                if (pastPayment.payment_type_code != "oth")
                                                                {
                                                                    WaitCheck(1);
                                                                    _log.Info(
                                                                        string.Format(
                                                                            " *** Sending SIMS Payment {0} to Xero ***",
                                                                            pastPayment.order_payment_id));
                                                                    Tuple<Payment, string> paymentCreateReturn =
                                                                        xeroIntegration.CreatePayment(xeroPayment,
                                                                            options.TransmitToXero);
                                                                    xeroPayment = paymentCreateReturn.Item1;
                                                                    paymentAudit.Action =
                                                                        string.Format(
                                                                            "INVOICE STATUS: {0} - MATCHED PAYMENT: {1} - ACTION: {2}",
                                                                            matchInvoice.Status, foundMatchedPayment,
                                                                            paymentCreateReturn.Item2);
                                                                }
                                                                else
                                                                {
                                                                    _log.Info(
                                                                        " *** SIMS Payment type = \'oth\' Not sending to Xero ***");
                                                                    xeroPayment.Id =
                                                                        new Guid("99999999-9999-9999-9999-999999999999");
                                                                    Common.RecordXeroPaymentControl(
                                                                        pastOrder.order_number, pastPayment,
                                                                        xeroPayment.Id.ToString());
                                                                }
                                                                if (xeroPayment.Id != Guid.Empty)
                                                                {
                                                                    Common.RecordXeroPaymentControl(
                                                                        pastOrder.order_number, pastPayment,
                                                                        xeroPayment.Id.ToString());
                                                                }

                                                                paymentAudit.CheckNumber = pastPayment.check_number;
                                                                paymentAudit.OrderId = pastOrder.order_id;
                                                                paymentAudit.OrderNumber = pastOrder.order_number;
                                                                paymentAudit.PaymentAmount = pastPayment.payment_amount;
                                                                paymentAudit.PaymentDate = pastPayment.payment_date;
                                                                paymentAudit.PaymentID = pastPayment.order_payment_id;
                                                                paymentAudit.PaymentType = pastPayment.payment_type_code;
                                                                paymentAudit.XeroPaymentId = xeroPayment.Id.ToString();

                                                                paymentCsv.WriteRecord(paymentAudit);
                                                            }
                                                            else
                                                            {
                                                                _log.Info(
                                                                    string.Format(
                                                                        " *** Found Existing Payment: {0} - Xero Invoice Status: {1} - Not sending to Xero ***",
                                                                        foundMatchedPayment, matchInvoice.Status));
                                                                paymentAudit = new PaymentAudit();
                                                                if (!paymentHeaderWritten)
                                                                {
                                                                    paymentCsv.WriteHeader(paymentAudit.GetType());
                                                                    paymentHeaderWritten = true;
                                                                }
                                                                paymentAudit.CheckNumber = pastPayment.check_number;
                                                                paymentAudit.OrderId = pastOrder.order_id;
                                                                paymentAudit.OrderNumber = pastOrder.order_number;
                                                                paymentAudit.PaymentAmount = pastPayment.payment_amount;
                                                                paymentAudit.PaymentDate = pastPayment.payment_date;
                                                                paymentAudit.PaymentID = pastPayment.order_payment_id;
                                                                paymentAudit.PaymentType = pastPayment.payment_type_code;
                                                                paymentAudit.XeroPaymentId = Guid.Empty.ToString();
                                                                paymentAudit.Action =
                                                                    string.Format(
                                                                        "INVOICE STATUS: {0} - MATCHED PAYMENT: {1} - ACTION: NOT SENT",
                                                                        matchInvoice.Status, foundMatchedPayment);

                                                                paymentCsv.WriteRecord(paymentAudit);
                                                            }

                                                        }

                                                    }
                                                    else //This inovice was paid, so we need to do some updating.
                                                    {
                                                        _log.Info(" *** Paid Invoice ***");
                                                        paymentAudit = new PaymentAudit();
                                                        if (!paymentHeaderWritten)
                                                        {
                                                            paymentCsv.WriteHeader(paymentAudit.GetType());
                                                            paymentHeaderWritten = true;
                                                        }
                                                        paymentAudit.CheckNumber = pastPayment.check_number;
                                                        paymentAudit.OrderId = pastOrder.order_id;
                                                        paymentAudit.OrderNumber = pastOrder.order_number;
                                                        paymentAudit.PaymentAmount = pastPayment.payment_amount;
                                                        paymentAudit.PaymentDate = pastPayment.payment_date;
                                                        paymentAudit.PaymentID = pastPayment.order_payment_id;
                                                        paymentAudit.PaymentType = pastPayment.payment_type_code;
                                                        paymentAudit.XeroPaymentId = matchInvoice.Id.ToString();
                                                        paymentAudit.Action = "ALREADY PAID";

                                                        paymentCsv.WriteRecord(paymentAudit);
                                                        //Update the Payment with the Xero Payment ID
                                                        Payment foundPayment =
                                                            xeroIntegration.FindXeroPaymentByReference(
                                                                matchInvoice.Reference);
                                                        if (foundPayment != null)
                                                        {
                                                            Common.RecordXeroPaymentControl(pastOrder.order_number,
                                                                pastPayment, foundPayment.Id.ToString());
                                                        }
                                                        else
                                                        {
                                                            _log.ErrorFormat(
                                                                "Payment <{0}> found as Paid in Xero, but invoice could not be found by Reference Number <{1}>",
                                                                foundPayment.Id,
                                                                foundPayment.Invoice.Reference);
                                                            throw new Exception(
                                                                "Payment Found as Paid in Xero, but invoice couldn't be found in Xero by Reference Number");
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            _log.Info(string.Format("*** Past Order <{0}> had a status of CLOSED",pastOrder.order_number));
                                        }
                                    }
                                    else
                                    {
                                        _log.Info(string.Format("*** Could not Find Order <{0}> for pulled Payment",pastOrder.order_number));
                                    }
                                }
                                else
                                {
                                    _log.Info(string.Format("*** Payment already processed: Ref:{0}", pastPayment.order_payment_id));
                                }
                            }
                            catch (ValidationException valEx)
                            {
                                var st = new StackTrace(valEx, true);
                                var frame = st.GetFrame(0);
                                var line = frame.GetFileLineNumber();
                                ExceptionAudit exceptionAudit = LogExceptionData(valEx, null, null, paymentAudit);
                                if (!exceptionHeaderWritten)
                                {
                                    exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                    exceptionHeaderWritten = true;
                                }
                                exceptionCsv.WriteRecord(exceptionAudit);
                                _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line,
                                    valEx.Message);
                                _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(valEx));
                            }
                            catch (Exception ex)
                            {
                                var st = new StackTrace(ex, true);
                                var frame = st.GetFrame(0);
                                var line = frame.GetFileLineNumber();
                                ExceptionAudit exceptionAudit = LogExceptionData(ex, null, null, paymentAudit);
                                if (!exceptionHeaderWritten)
                                {
                                    exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                    exceptionHeaderWritten = true;
                                }
                                exceptionCsv.WriteRecord(exceptionAudit);
                                _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line,
                                    ex.Message);
                                _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(ex));
                            }
                        }
                    //else
                    //{
                    //    _log.Info("*****  Not Transmitting to Xero.  Skipping Check for Back Payments ********");
                    //}
                    customerAuditTextWriter.Close();
                    invoiceAuditTextWriter.Close();
                    paymentAuditTextWriter.Close();
                    exceptionAuditTextWriter.Close();
                    _log.Info("***** Sending Daily Results Email *****");
                    if (emailResults)
                    {
                        SendCompleteEmail(emailTo, auditCustomerFile, auditInvoiceFile, auditPaymentFile, exceptionFile);
                    }
                    TimeSpan elapsedTime = DateTime.Now.Subtract(startTime);

                    _log.Info("*******  Process Completed ********");
                    _log.Info(string.Format("** Elapsed Time: {0:c}", elapsedTime));
                    _log.Info("*************************************************");

                    var rootAppender = ((Hierarchy)LogManager.GetRepository())
                                         .Root.Appenders.OfType<FileAppender>()
                                         .FirstOrDefault();

                    string filename = rootAppender != null ? rootAppender.File : string.Empty;
                    SendSupportCompleteEmail(exceptionFile, filename);
                }
                catch (Exception ex)
                {
                    
                    
                    _log.Error("!!!!!!!! ERROR !!!!!!!!!!");
                    _log.ErrorFormat(" Error: {0}", ex.Message);
                    _log.ErrorFormat(" Error Inner Exception: {0}", Utilities.FlattenException(ex));
                    _log.Error("!!!!!!! END OF ERROR !!!!!!!!");
                    TimeSpan elapsedTime = DateTime.Now.Subtract(startTime);

                    _log.Info("*******  Process Completed ********");
                    _log.Info(string.Format("** Elapsed Time: {0:c}", elapsedTime));
                    _log.Info("*************************************************");
                    //Get the log file so we can send it to Support.
                    var rootAppender = ((Hierarchy)LogManager.GetRepository())
                                         .Root.Appenders.OfType<FileAppender>()
                                         .FirstOrDefault();

                    string filename = rootAppender != null ? rootAppender.File : string.Empty;
                    SendSupportCompleteEmail(filename);  //Sends just the Log file.
                }
            }
            
            //Console.ReadKey();
        }

        static void WaitCheck(int transCount)
        {
            _transactionCount += transCount;

            _elapsedTimeSpan = DateTime.Now.Subtract(_lastTime);
            _lastTime = DateTime.Now;
            if (_elapsedTimeSpan.Seconds < 60) // We need to check about waiting.
            {
                if (_transactionCount >= 60)
                {
                    _transactionCount = 0;
                    //Need to wait.
                    int waitTime = 60 - _elapsedTimeSpan.Seconds;
                    Thread.Sleep(waitTime * 1000);
                }
            }
        }

        private static void SendCompleteEmail(string[] emailTo, string customerAttch, string invoiceAttch, string paymentAttch, string exceptionAttch)
        {

            var mailServer = ConfigurationManager.AppSettings["SMTPServer"];
            var mailPort = int.Parse(ConfigurationManager.AppSettings["SMTPPort"]);
            var mailAuth = ConfigurationManager.AppSettings["SMTPAuth"];
            var mailPass = ConfigurationManager.AppSettings["SMTPPass"];
            var fromEmail = ConfigurationManager.AppSettings["FromEmail"];
            var fromText = ConfigurationManager.AppSettings["FromText"];

            string primaryEmail = emailTo[0];
            MailMessage m = new MailMessage(
            new MailAddress(fromEmail, fromText),
            new MailAddress(primaryEmail));
            foreach (string email in emailTo.Skip(1))
            {
                m.To.Add(email);    
            }
            m.Subject = "Xero Integration Complete";
            m.Body = "Nightly Xero Integration has completed.  Attached are the nightly files.";

            m.Attachments.Add(new System.Net.Mail.Attachment(customerAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(invoiceAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(paymentAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(exceptionAttch));

            m.IsBodyHtml = true;
            SmtpClient smtp = new SmtpClient(mailServer, mailPort)
            {
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(mailAuth, mailPass),
                EnableSsl = false
            };

            smtp.Send(m);
        }

        private static void SendSupportCompleteEmail(string exceptionAttch, string logFileAttach)
        {

            var mailServer = ConfigurationManager.AppSettings["SMTPServer"];
            var mailPort = int.Parse(ConfigurationManager.AppSettings["SMTPPort"]);
            var mailAuth = ConfigurationManager.AppSettings["SMTPAuth"];
            var mailPass = ConfigurationManager.AppSettings["SMTPPass"];
            var fromEmail = ConfigurationManager.AppSettings["FromEmail"];
            var fromText = ConfigurationManager.AppSettings["FromText"];

            string primaryEmail = ConfigurationManager.AppSettings["SupportEmail"];

            MailMessage m = new MailMessage(
                new MailAddress(fromEmail, fromText),
                new MailAddress(primaryEmail))
            {
                Subject = "SUPPORT - Xero Integration Complete",
                Body = "Nightly Xero Integration has completed.  Attached is the Exception files."
            };


            m.Attachments.Add(new System.Net.Mail.Attachment(exceptionAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(logFileAttach));

            m.IsBodyHtml = true;
            SmtpClient smtp = new SmtpClient(mailServer, mailPort)
            {
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(mailAuth, mailPass),
                EnableSsl = false
            };

            smtp.Send(m);
        }

        private static void SendSupportCompleteEmail(string logFileAttach)
        {

            var mailServer = ConfigurationManager.AppSettings["SMTPServer"];
            var mailPort = int.Parse(ConfigurationManager.AppSettings["SMTPPort"]);
            var mailAuth = ConfigurationManager.AppSettings["SMTPAuth"];
            var mailPass = ConfigurationManager.AppSettings["SMTPPass"];
            var fromEmail = ConfigurationManager.AppSettings["FromEmail"];
            var fromText = ConfigurationManager.AppSettings["FromText"];

            string primaryEmail = ConfigurationManager.AppSettings["SupportEmail"];

            MailMessage m = new MailMessage(
                new MailAddress(fromEmail, fromText),
                new MailAddress(primaryEmail))
            {
                Subject = "SUPPORT - Xero Integration Complete",
                Body = "Nightly Xero Integration has completed.  Attached is the Exception files."
            };


            m.Attachments.Add(new System.Net.Mail.Attachment(logFileAttach));

            m.IsBodyHtml = true;
            SmtpClient smtp = new SmtpClient(mailServer, mailPort)
            {
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(mailAuth, mailPass),
                EnableSsl = false
            };

            smtp.Send(m);
        }

        private static ExceptionAudit LogExceptionData(Exception ex, CustomerAudit customerAudit, InvoiceAudit invoiceAudit,
            PaymentAudit paymentAudit)
        {
            ExceptionAudit exceptionAudit = new ExceptionAudit();
            if (invoiceAudit != null)
            {
                exceptionAudit.OrderId = invoiceAudit.OrderId.ToString();
                exceptionAudit.OrderNumber = invoiceAudit.OrderNumber;
                exceptionAudit.InvoiceCreateDate = invoiceAudit.CreateDate;
                exceptionAudit.InvoiceDueDate = invoiceAudit.InvoiceDueDate;
                exceptionAudit.LineItemCount = invoiceAudit.LineItemCount;
                exceptionAudit.ReferenceNbr = invoiceAudit.ReferenceNbr;
                exceptionAudit.InvoiceAmt = invoiceAudit.InvoiceAmt;
                exceptionAudit.XeroInvoiceId = invoiceAudit.XeroInvoiceId;
            }
            if (customerAudit != null)
            {
                exceptionAudit.CustomerID = customerAudit.CustomerID;
                exceptionAudit.XeroCustomerID = customerAudit.XeroCustomerID;
                exceptionAudit.CustomerName = customerAudit.CustomerName;
                exceptionAudit.Email = customerAudit.Email;
                exceptionAudit.Address = customerAudit.Address;
                exceptionAudit.City = customerAudit.City;
                exceptionAudit.State = customerAudit.State;
                exceptionAudit.Zip = customerAudit.Zip;
                exceptionAudit.ContactName = customerAudit.ContactName;
                exceptionAudit.ContactEmail = customerAudit.ContactEmail;
                exceptionAudit.ContactPhone = customerAudit.ContactPhone;
            }
            if (paymentAudit != null)
            {
                exceptionAudit.OrderId = paymentAudit.OrderId.ToString();
                exceptionAudit.OrderNumber = paymentAudit.OrderNumber;
                exceptionAudit.PaymentID  = paymentAudit.PaymentID ;
                exceptionAudit.XeroPaymentId  = paymentAudit.XeroPaymentId ;
                exceptionAudit.PaymentDate  = paymentAudit.PaymentDate ;
                exceptionAudit.PaymentType  = paymentAudit.PaymentType ;
                exceptionAudit.CheckNumber  = paymentAudit.CheckNumber ;
                exceptionAudit.PaymentAmount  = paymentAudit.PaymentAmount ;
            }

            exceptionAudit.ExceptionMessage = Utilities.FlattenException(ex);

            return exceptionAudit;
        }
    }
}
