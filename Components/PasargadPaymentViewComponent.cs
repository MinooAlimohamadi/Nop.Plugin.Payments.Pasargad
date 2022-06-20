using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Pasargad.Components
{
    [ViewComponent(Name = "PasargadPayment")]
    public class PasargadPaymentViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.Pasargad/Views/PaymentInfo.cshtml");
        }
    }
}
