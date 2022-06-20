using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Pasargad.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Nop.Plugin.Payments.Pasargad.Controllers
{
    public class PasargadPaymentController : BasePaymentController
    {
        #region Fields
        private readonly ILocalizationService _localizationService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly IPaymentService _paymentService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly INotificationService _notificationService;
        private readonly PasargadPaymentSettings _pasargadPaymentSettings;
        private readonly PaymentSettings _paymentSettings;
        #endregion

        #region Ctor

        public PasargadPaymentController(
            ILocalizationService localizationService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWorkContext workContext,
            IPaymentService paymentService,
            IPaymentPluginManager paymentPluginManager,
            IOrderService orderService,
            INotificationService notificationService,
            IOrderProcessingService orderProcessingService,
            PasargadPaymentSettings pasargadPaymentSettings,
            PaymentSettings paymentSettings
            )
        {
            this._localizationService = localizationService;
            this._permissionService = permissionService;
            this._settingService = settingService;
            this._storeContext = storeContext;
            this._workContext = workContext;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._notificationService = notificationService;
            this._orderProcessingService = orderProcessingService;
            this._pasargadPaymentSettings = pasargadPaymentSettings;
            this._paymentSettings = paymentSettings;
            this._paymentPluginManager = paymentPluginManager;
        }
        #endregion

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var setting = await _settingService.LoadSettingAsync<PasargadPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                PrivateKey = setting.PrivateKey,
                TerminalCode = setting.TerminalCode,
                MerchantCode = setting.MerchantCode,
            };

            if (storeScope <= 0)
                return View("~/Plugins/Payments.Pasargad/Views/Configure.cshtml", model);

            model.PrivateKey_OverrideForStore = await _settingService.SettingExistsAsync(setting, x => x.PrivateKey, storeScope);
            model.MerchantCode_OverrideForStore = await _settingService.SettingExistsAsync(setting, x => x.MerchantCode, storeScope);
            model.TerminalCode_OverrideForStore = await _settingService.SettingExistsAsync(setting, x => x.TerminalCode, storeScope);

            return View("~/Plugins/Payments.Pasargad/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [AutoValidateAntiforgeryToken]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //model.AdditionalFee = Convert.ToDecimal(model.AdditionalFee);

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var pasargadPaymentSettings = await _settingService.LoadSettingAsync<PasargadPaymentSettings>(storeScope);


            //save settings
            pasargadPaymentSettings.MerchantCode = model.MerchantCode;
            pasargadPaymentSettings.PrivateKey = model.PrivateKey;
            pasargadPaymentSettings.TerminalCode = model.TerminalCode;

            /* We do not clear cache after each setting update.
            * This behavior can increase performance because cached settings will not be cleared 
            * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(pasargadPaymentSettings, x => x.MerchantCode, model.MerchantCode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(pasargadPaymentSettings, x => x.PrivateKey, model.PrivateKey_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(pasargadPaymentSettings, x => x.TerminalCode, model.TerminalCode_OverrideForStore, storeScope, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        public async Task<IActionResult> BankCallback(IFormCollection form)
        {
            try
            {
                //اطلاعات زیر جهت ارجاع فاکتور از بانک می باشد
                var invoiceNumber = CommonHelper.EnsureNotNull(Request.Query["iN"]); // شماره فاکتور
                var invoiceDate = CommonHelper.EnsureNotNull(Request.Query["iD"]); // تاریخ فاکتور
                var invoiceUID = CommonHelper.EnsureNotNull(Request.Query["tref"]); // شماره مرجع

                var order = await _orderService.GetOrderByIdAsync(Convert.ToInt32(invoiceNumber));
                if (order == null)
                    throw new NopException(string.Format("The order ID {0} doesn't exists", invoiceNumber));

                var strXML = ReadPaymentResult(invoiceUID);

                if (strXML == "")
                {
                    await _orderService.InsertOrderNoteAsync(new OrderNote()
                    {
                        Note = "واریز وجه : تراکنش انجام نشد ",
                        DisplayToCustomer = true,
                        OrderId = order.Id,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    await _orderService.UpdateOrderAsync(order);
                    return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                }
                else
                {
                    var oXml = new XmlDocument();
                    oXml.LoadXml(strXML);

                    var oElResult = (XmlElement)oXml.SelectSingleNode("//result"); //نتیجه تراکنش
                    var oElTraceNumber = (XmlElement)oXml.SelectSingleNode("//traceNumber"); //شماره پیگیری
                    var txNreferenceNumber = (XmlElement)oXml.SelectSingleNode("//referenceNumber"); //شماره ارجاع

                    //تراکنش کنسل شود
                    if (oElResult.InnerText == "False")
                    {
                        await _orderService.InsertOrderNoteAsync(new OrderNote()
                        {
                            Note = "واریز وجه : تراکنش انجام نشد ",
                            DisplayToCustomer = true,
                            OrderId = order.Id,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        await _orderService.UpdateOrderAsync(order);
                        return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                    }
                    else
                    {
                        order.PaymentStatus = PaymentStatus.Pending;
                        await _orderService.InsertOrderNoteAsync(new OrderNote()
                        {
                            Note = (oElResult != null ? "نتیجه تراکنش : " + oElResult.InnerText + Environment.NewLine : "")
                            + (oElTraceNumber != null ? "شماره پیگیری : " + oElTraceNumber.InnerText + Environment.NewLine : "")
                            + (txNreferenceNumber != null ? "شماره ارجاع : " + txNreferenceNumber.InnerText : ""),
                            DisplayToCustomer = true,
                            OrderId = order.Id,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        await _orderService.UpdateOrderAsync(order);

                        #region Confirmation
                        var merchantCode = _pasargadPaymentSettings.MerchantCode;
                        var terminalCode = _pasargadPaymentSettings.TerminalCode;
                        var privateKey = _pasargadPaymentSettings.PrivateKey;

                        var amount = long.Parse(order.OrderTotal.ToString("0"));
                        var timeStamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                        var rsa = new RSACryptoServiceProvider();
                        rsa.FromXmlString(privateKey);

                        var data = "#" + merchantCode + "#" + terminalCode + "#" + invoiceNumber +
                            "#" + invoiceDate + "#" + amount + "#" + timeStamp + "#";

                        var signedData = rsa.SignData(Encoding.UTF8.GetBytes(data), new SHA1CryptoServiceProvider());
                        var signedString = Convert.ToBase64String(signedData);

                        var request = (HttpWebRequest)WebRequest.Create("https://pep.shaparak.ir/VerifyPayment.aspx");
                        var text = "InvoiceNumber=" + invoiceNumber + "&InvoiceDate=" +
                                    invoiceDate + "&MerchantCode=" + merchantCode + "&TerminalCode=" +
                                    terminalCode + "&Amount=" + amount + "&TimeStamp=" + timeStamp + "&Sign=" + signedString;
                        var textArray = Encoding.UTF8.GetBytes(text);
                        request.Method = "POST";
                        request.ContentType = "application/x-www-form-urlencoded";
                        request.ContentLength = textArray.Length;
                        request.GetRequestStream().Write(textArray, 0, textArray.Length);
                        var response = (HttpWebResponse)request.GetResponse();
                        var reader = new StreamReader(response.GetResponseStream());
                        var result = reader.ReadToEnd();

                        if (null != result)
                        {
                            var resultXml = new XmlDocument();
                            resultXml.LoadXml(result);
                            var res = (XmlElement)resultXml.SelectSingleNode("//result");
                            var resMessage = (XmlElement)resultXml.SelectSingleNode("//resultMessage");

                            order.PaymentStatus = PaymentStatus.Paid;
                            await _orderService.InsertOrderNoteAsync(new OrderNote
                            {
                                Note = "تایید پرداخت : " + res.InnerText + Environment.NewLine + resMessage.InnerText,
                                DisplayToCustomer = true,
                                OrderId = order.Id,
                                CreatedOnUtc = DateTime.UtcNow
                            });
                            await _orderService.UpdateOrderAsync(order);
                            return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                        }

                        #endregion
                    }
                }
            }
            catch (Exception ex)
            {
                throw new NopException(ex.Message + " _ " + ex.StackTrace);
            }

            return RedirectToAction("Index", "Home", new { area = "" });
        }

        protected static string ReadPaymentResult(string invoiceUID)
        {
            var request = (HttpWebRequest)WebRequest.Create("https://pep.shaparak.ir/CheckTransactionResult.aspx");
            var text = "invoiceUID=" + invoiceUID;
            var textArray = Encoding.UTF8.GetBytes(text);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = textArray.Length;
            request.GetRequestStream().Write(textArray, 0, textArray.Length);
            var response = (HttpWebResponse)request.GetResponse();
            var reader = new StreamReader(response.GetResponseStream());
            var result = reader.ReadToEnd();
            return result;
        }
    }
}
