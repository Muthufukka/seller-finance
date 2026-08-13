using Microsoft.AspNetCore.Mvc;

namespace SellerFinance.Api;

public static class ApiProblemDetails
{
    public static object? Normalize(object? result,HttpContext context)
    {
        if(result is not IStatusCodeHttpResult status||status.StatusCode is not(>=400 and <=599))return result;
        if(result is IValueHttpResult value&&value.Value is ProblemDetails)return result;
        var title=result is IValueHttpResult valueResult?ReadTitle(valueResult.Value):null;
        return Results.Problem(
            statusCode:status.StatusCode,
            title:String.IsNullOrWhiteSpace(title)?DefaultTitle(status.StatusCode.Value):title,
            type:$"https://httpstatuses.io/{status.StatusCode.Value}",
            instance:context.Request.Path,
            extensions:new Dictionary<string,object?>{{"traceId",context.TraceIdentifier}});
    }

    private static string? ReadTitle(object? value)
    {
        if(value is string text)return text;
        return value?.GetType().GetProperty("title",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.IgnoreCase)?.GetValue(value)?.ToString();
    }

    private static string DefaultTitle(int statusCode)=>statusCode switch
    {
        400=>"Некорректный запрос",401=>"Требуется авторизация",403=>"Недостаточно прав",404=>"Ресурс не найден",409=>"Конфликт состояния",429=>"Слишком много запросов",_ when statusCode>=500=>"Внутренняя ошибка сервиса",_=>"Запрос не выполнен"
    };
}
