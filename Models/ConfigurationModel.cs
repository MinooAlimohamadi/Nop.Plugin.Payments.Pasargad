using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;


namespace Nop.Plugin.Payments.Pasargad.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        //  [AllowHtml]
        [DataType(DataType.MultilineText)]
        [NopResourceDisplayName("Plugins.Payments.Pasargad.PrivateKey")]
        public string PrivateKey { get; set; }
        public bool PrivateKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Pasargad.TerminalCode")]
        public string TerminalCode { get; set; }
        public bool TerminalCode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Pasargad.MerchantCode")]
        public string MerchantCode { get; set; }
        public bool MerchantCode_OverrideForStore { get; set; }

        //[NopResourceDisplayName("Plugins.Payments.Pasargad.AdditionalFee")]
        //public decimal AdditionalFee { get; set; }

        //[NopResourceDisplayName("Plugins.Payments.Pasargad.AdditionalFeePercentage")]
        //public bool AdditionalFeePercentage { get; set; }
    }
}
