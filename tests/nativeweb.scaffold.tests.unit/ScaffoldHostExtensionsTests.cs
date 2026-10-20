using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// The host-wiring extensions. The rejection guard is the one with behaviour worth
/// asserting: UseAntiforgery() validates but does not reject, so without this the
/// caller gets a 500 from the form reader where a 400 belongs.
/// </summary>
public sealed class ScaffoldHostExtensionsTests
{
    private sealed class FailedValidation : IAntiforgeryValidationFeature
    {
        public bool IsValid => false;

        public Exception? Error => new AntiforgeryValidationException("no token");
    }

    private sealed class PassedValidation : IAntiforgeryValidationFeature
    {
        public bool IsValid => true;

        public Exception? Error => null;
    }

    // Builds just the guard, with a terminal that records whether it was reached.
    private static (RequestDelegate Pipeline, Func<bool> ReachedEndpoint) Guard()
    {
        bool reached = false;
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        ApplicationBuilder builder = new(services);

        builder.UseAntiforgeryRejection();
        builder.Run(context =>
        {
            reached = true;
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        return (builder.Build(), () => reached);
    }

    [Fact]
    public async Task Guard_TurnsAFailedValidationIntoA400()
    {
        (RequestDelegate pipeline, Func<bool> reached) = Guard();
        var context = new DefaultHttpContext();
        context.Features.Set<IAntiforgeryValidationFeature>(new FailedValidation());

        await pipeline(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        // The stronger half: the endpoint must never run. A 400 written after the
        // handler already mutated the store would be a rejection in name only.
        Assert.False(reached(), "the endpoint ran despite a failed antiforgery check");
    }

    [Fact]
    public async Task Guard_LetsAPassedValidationThrough()
    {
        (RequestDelegate pipeline, Func<bool> reached) = Guard();
        var context = new DefaultHttpContext();
        context.Features.Set<IAntiforgeryValidationFeature>(new PassedValidation());

        await pipeline(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(reached());
    }

    // Registering it unconditionally has to be safe, because UseScaffolding always
    // does: a GET carries no antiforgery feature at all.
    [Fact]
    public async Task Guard_IsANoOpWhenNoValidationRan()
    {
        (RequestDelegate pipeline, Func<bool> reached) = Guard();

        await pipeline(new DefaultHttpContext());

        Assert.True(reached());
    }

    // Asserted on the descriptors rather than on a built provider: resolving
    // IAntiforgery drags in data protection, logging and options, none of which this
    // is testing. What AddScaffolding owes the caller is the registration.
    [Fact]
    public void AddScaffolding_RegistersAntiforgeryAndAuthorization()
    {
        IServiceCollection services = new ServiceCollection().AddScaffolding();

        Assert.Contains(services, d => d.ServiceType == typeof(IAntiforgery));
        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.AspNetCore.Authorization.IAuthorizationService));
    }

    // The deliberate omission, asserted so it cannot be "helpfully" added later:
    // who may reach /admin is the application's decision, not the library's.
    [Fact]
    public void AddScaffolding_RegistersNoAuthenticationScheme()
    {
        IServiceCollection services = new ServiceCollection().AddScaffolding();

        Assert.DoesNotContain(
            services,
            d => d.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService));
    }
}
