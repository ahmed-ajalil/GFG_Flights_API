using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GFG.Flights.Api.Infrastructure;

public sealed class DateOnlyModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var v = ctx.ValueProvider.GetValue(ctx.ModelName);
        if (v == ValueProviderResult.None) return Task.CompletedTask;

        var str = v.FirstValue;
        if (DateOnly.TryParse(str, out var d) || DateOnly.TryParseExact(str, "yyyy-MM-dd", out d))
        {
            ctx.Result = ModelBindingResult.Success(d);
        }
        else
        {
            ctx.ModelState.AddModelError(ctx.ModelName, "Invalid date (use yyyy-MM-dd).");
        }
        return Task.CompletedTask;
    }
}