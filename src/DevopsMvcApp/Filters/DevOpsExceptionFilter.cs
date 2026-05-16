using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using DevopsMvcApp.Controllers;

namespace DevopsMvcApp.Filters;

public class DevOpsExceptionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception != null && !executed.ExceptionHandled && context.Controller is DevOpsController c)
        {
            c.TempData["Error"] = executed.Exception.Message;
            executed.Result = new RedirectToActionResult("Dashboard", "DevOps", null);
            executed.ExceptionHandled = true;
        }
    }
}
