using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Api.Endpoints;
using Nebula.Application.Common;

namespace Nebula.IntegrationTests;

public sealed class ValidationFilterTests
{
    [Fact]
    public async Task Invalid_argument_throws_validation_with_first_error_per_camel_case_field()
    {
        var filter = new ValidationFilter<Sample>("Sample is invalid");
        var context = CreateContext(new Sample("", -1, new SampleChild("")));

        var error = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
            await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("handler ran")));

        Assert.Equal(422, error.Status);
        Assert.Equal("validation", error.Code);
        Assert.Equal("Sample is invalid", error.Message);
        Assert.NotNull(error.Details);
        Assert.Equal("Name is required", error.Details["name"]);
        Assert.Equal("Unit price must be positive", error.Details["unitPrice"]);
        Assert.Equal("Child size is required", error.Details["child.sizeLabel"]);
        Assert.Equal(3, error.Details.Count);
    }

    [Fact]
    public async Task Valid_argument_reaches_the_handler()
    {
        var filter = new ValidationFilter<Sample>("Sample is invalid");
        var context = CreateContext(new Sample("Tee", 10, new SampleChild("M")));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("handler ran"));

        Assert.Equal("handler ran", result);
    }

    private static DefaultEndpointFilterInvocationContext CreateContext(Sample sample)
    {
        var services = new ServiceCollection()
            .AddSingleton<IValidator<Sample>, SampleValidator>()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return new DefaultEndpointFilterInvocationContext(http, sample);
    }

    public sealed record Sample(string Name, decimal UnitPrice, SampleChild Child);

    public sealed record SampleChild(string SizeLabel);

    private sealed class SampleValidator : AbstractValidator<Sample>
    {
        public SampleValidator()
        {
            RuleFor(s => s.Name).NotEmpty().WithMessage("Name is required").MinimumLength(2).WithMessage("Too short");
            RuleFor(s => s.UnitPrice).GreaterThan(0).WithMessage("Unit price must be positive");
            RuleFor(s => s.Child.SizeLabel).NotEmpty().WithMessage("Child size is required");
        }
    }
}
