using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class ApiProblemDetailsTests
{
    [Fact]
    public void Normalize_Converts_Conflict_Payload_And_Preserves_Existing_ProblemDetails()
    {
        var context=new DefaultHttpContext();context.Request.Path="/api/v1/example";context.TraceIdentifier="trace-1";
        var normalized=Assert.IsAssignableFrom<IValueHttpResult>(ApiProblemDetails.Normalize(Results.Conflict(new{title="Конфликт"}),context));var problem=Assert.IsType<ProblemDetails>(normalized.Value);Assert.Equal(409,problem.Status);Assert.Equal("Конфликт",problem.Title);Assert.Equal("/api/v1/example",problem.Instance);Assert.Equal("trace-1",problem.Extensions["traceId"]);
        var existing=Results.Problem("Already normalized",statusCode:422);Assert.Same(existing,ApiProblemDetails.Normalize(existing,context));
    }
}
