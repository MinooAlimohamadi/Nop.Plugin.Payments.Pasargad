using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Web.Framework;

namespace Nop.Plugin.Payments.Pasargad
{
    public class PasargadPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IWebHelper _webHelper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly PasargadPaymentSettings _pasargadPaymentSettings;
        #endregion

        #region Ctor
        public PasargadPaymentProcessor(
            ILocalizationService localizationService,
            ISettingService settingService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IWebHelper webHelper,
            IHttpContextAccessor httpContextAccessor,
            PasargadPaymentSettings pasargadPaymentSettings)
        {
            this._localizationService = localizationService;
            this._settingService = settingService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._webHelper = webHelper;
            this._pasargadPaymentSettings = pasargadPaymentSettings;
            _httpContextAccessor = httpContextAccessor;
        }
        #endregion

        #region Methods
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = PaymentStatus.Pending;
            return Task.FromResult(new ProcessPaymentResult());
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            try
            {
                //دریافت تنظیمات از web.config
                var merchantCode = _pasargadPaymentSettings.MerchantCode;
                var terminalCode = _pasargadPaymentSettings.TerminalCode;
                var redirectAddress = _webHelper.GetStoreLocation(false) + "PasargadPayment/BankCallback";
                var privateKey = _pasargadPaymentSettings.PrivateKey;

                var amount = postProcessPaymentRequest.Order.OrderTotal.ToString("0");

                //تاریخ فاکتور و زمان اجرای عملیات از سیستم گرفته می شود
                //شما می توانید تاریخ فاکتور را به صورت دستی وارد نمایید 
                var timeStamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                var invoiceDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                var invoiceNumber = postProcessPaymentRequest.Order.Id;
                // 1003 Buy
                // 1004 Back buy
                var actionIs = "1003";

                var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(privateKey);

                var data = "#" + merchantCode + "#" + terminalCode + "#" + invoiceNumber +
                    "#" + invoiceDate + "#" + amount + "#" + redirectAddress + "#" + actionIs + "#" + timeStamp + "#";

                var signedData = rsa.SignData(System.Text.Encoding.UTF8.GetBytes(data), new SHA1CryptoServiceProvider());

                var signedString = Convert.ToBase64String(signedData);

                var remotePostHelper =new RemotePost(_httpContextAccessor,_webHelper);
                remotePostHelper.FormName = "PasargadBankForm";
                remotePostHelper.Url = "https://pep.shaparak.ir/gateway.aspx";

                remotePostHelper.Add("merchantCode", merchantCode);
                remotePostHelper.Add("terminalCode", terminalCode);
                remotePostHelper.Add("amount", amount);
                remotePostHelper.Add("redirectAddress", redirectAddress);
                remotePostHelper.Add("invoiceNumber", invoiceNumber + "");
                remotePostHelper.Add("invoiceDate", invoiceDate);
                remotePostHelper.Add("action", actionIs);
                remotePostHelper.Add("sign", signedString);
                remotePostHelper.Add("timeStamp", timeStamp);

                remotePostHelper.Post();
                //fake
                await _settingService.SaveSettingAsync(new PasargadPaymentSettings { MerchantCode=merchantCode,TerminalCode=terminalCode,PrivateKey=privateKey});
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(false);
        }

        public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult((decimal)0);
        }

        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
        }

        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Pasargad.PaymentMethodDescription");

        }
        #endregion

        #region Properties
        public bool SupportCapture
        {
            get { return false; }
        }
        public bool SupportPartiallyRefund { get { return false; } }
        public bool SupportRefund { get { return false; } }
        public bool SupportVoid { get { return false; } }
        public RecurringPaymentType RecurringPaymentType { get { return RecurringPaymentType.NotSupported; } }
        public PaymentMethodType PaymentMethodType { get { return PaymentMethodType.Redirection; } }
        public bool SkipPaymentInfo { get { return false; } }

        #endregion

        #region Plugin
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PasargadPayment/Configure";
        }

        public void GetPublicViewComponent(out string viewComponentName)
        {
            viewComponentName = "PasargadPayment";
        }

        public override async Task InstallAsync()

        {
            //settings
            await _settingService.SaveSettingAsync(new PasargadPaymentSettings
            {
                PrivateKey = "",
                TerminalCode = "",
                MerchantCode = "",
                //AdditionalFee = 0,
                //AdditionalFeePercentage = false
            });

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Pasargad.PaymentMethodDescription"] = "از تمامی کارت های عضو شتاب توسط این درگاه می توانید پرداخت را انجام دهید",
                ["Plugins.Payments.Pasargad.PaymentMethodInfo"] = "کاربر گرامی در مرحله بعدی پس از تایید فاکتور شما به سایت بانک ارجاع داده شده و پس از پرداخت مبلغ به همین سایت بازگردانده می شوید",
                ["Plugins.Payments.Pasargad.PrivateKey"] = "کلید شخصی",
                ["Plugins.Payments.Pasargad.PrivateKey.Hint"] = "Private Key وارد کنید",
                ["Plugins.Payments.Pasargad.TerminalCode"] = "کد Terminal",
                ["Plugins.Payments.Pasargad.TerminalCode.Hint"] = "Terminal Code بانک پاسارگاد را وارد کنید.",
                ["Plugins.Payments.Pasargad.MerchantCode"] = "کد Merchant",
                ["Plugins.Payments.Pasargad.MerchantCode.Hint"] = "Merchant Code بانک پاسارگاد را وارد کنید.",
                ["Plugins.Payments.Pasargad.AdditionalFee"] = "هزینه ی اضافی",
                ["Plugins.Payments.Pasargad.AdditionalFee.Hint"] = "مبلغ هزینه اضافی را برای دریافت از مشتریان وارد کنید.",
                ["Plugins.Payments.Pasargad.AdditionalFeePercentage"] = "محاسبه به صورت درصد",
                ["Plugins.Payments.Pasargad.ConfigureName"] = "تنظیمات درگاه بانک پاسارگاد",

            });
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()

        {
            //settings
            await _settingService.DeleteSettingAsync<PasargadPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Pasargad");


            await base.UninstallAsync();
        }

        public string GetPublicViewComponentName()
        {
            return "PasargadPayment";
        }

        #endregion

    }
}
