using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GFG.Flights.Api.Infrastructure;

public sealed class DateOnlyModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => context.Metadata.ModelType == typeof(DateOnly) ? new DateOnlyModelBinder() : null;
}