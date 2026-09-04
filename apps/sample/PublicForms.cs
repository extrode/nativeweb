using Microsoft.AspNetCore.Antiforgery;

using NativeWeb.Forms;
using NativeWeb.Mail;
using NativeWeb.Render;
using NativeWeb.Security;
using NativeWeb.Web;

namespace NativeWeb.Sample;

/// <summary>The contact form. Every member is text, so every member is <c>string?</c>.</summary>
[Form]
public sealed partial record ContactForm(string? Name, string? Email, string? Message);

/// <summary>
/// The upload form: one caption and one picture, with the picture's ceiling and its
/// accepted types declared beside it rather than checked in the handler.
/// </summary>
/// <remarks>
/// The two limits are enforced at different moments and that is deliberate. The
/// ceiling aborts the read partway through; the accepted set is checked after the
/// form is read and before a byte reaches <see cref="UploadStore"/>. Spec 027 R4,
/// which was amended during planning to say so.
/// </remarks>
[Form]
public sealed partial record UploadForm(
    string? Caption,
    [FormField("picture")]
    [FormFile(MaxBytes = 1_048_576, Accept = ["image/png", "image/jpeg", "image/gif", "image/webp"])]
    UploadedFile Picture);

/// <summary>Step one of the survey.</summary>
[Form]
public sealed partial record SurveyStepOne(string? Role);

/// <summary>
/// Step two, carrying step one's answers back in <c>Carried</c>.
/// </summary>
/// <param name="Carried">The protected hidden field step two rendered.</param>
/// <param name="Comment">This step's own answer.</param>
[Form]
public sealed partial record SurveyStepTwo(string? Carried, string? Comment);

/// <summary>
/// The three public forms of spec 027: contact, upload, and a two-step survey.
/// </summary>
/// <remarks>
/// <para>
/// None of these is cacheable, for the reason <see cref="PublicArticleEndpoints"/>
/// gives at length: a page carrying an antiforgery token sets a cookie on the first
/// visit and would be stored and served to a stranger whose own cookie cannot match
/// it. So no <c>AsPublicPage</c> here, and every GET renders per visit.
/// </para>
/// <para>
/// Each route's lambda is one call to a method with a declared return type, for the
/// Request Delegate Generator reason <see cref="PublicSite"/> states.
/// </para>
/// </remarks>
internal static partial class PublicForms
{
    private const string SurveyPurpose = "role";

    /// <summary>How long a half-finished survey may sit before it has to be restarted.</summary>
    internal static TimeSpan CarriedLifetime { get; } = TimeSpan.FromMinutes(20);

    public static IEndpointRouteBuilder MapPublicForms(this IEndpointRouteBuilder endpoints)
    {
        // Each GET takes IAntiforgery as a parameter, and not for tidiness: a lambda
        // of HttpContext alone is a RequestDelegate to overload resolution, which
        // discards the IResult (ASP0016, a build error here). PublicSite.cs states
        // the same trap.
        endpoints.MapGet("/contact", (IAntiforgery antiforgery, HttpContext http) => ContactPageAsync(http, antiforgery))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        endpoints.MapFormPost<ContactForm>("/contact", SendContactAsync);

        endpoints.MapGet("/upload", (IAntiforgery antiforgery, HttpContext http) => UploadPageAsync(http, antiforgery))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        endpoints.MapFormPost<UploadForm>("/upload", StorePictureAsync);

        endpoints.MapGet("/uploads/{id}", (string id, HttpContext http) => ServeUploadAsync(id, http))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        endpoints.MapGet("/survey", (IAntiforgery antiforgery, HttpContext http) => SurveyPageAsync(http, antiforgery))
            .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        endpoints.MapFormPost<SurveyStepOne>("/survey/step-2", SurveyStepTwoAsync);

        endpoints.MapFormPost<SurveyStepTwo>("/survey", SurveyFinishAsync);

        return endpoints;
    }

    private static Task<IResult> ContactPageAsync(HttpContext http, IAntiforgery antiforgery) =>
        Task.FromResult(Contact(http, antiforgery, notice: "", error: "", name: "", email: "", message: ""));

    // The bot-trap contract (spec 019 R23): a tripped form takes the endpoint's
    // NORMAL-outcome path with no side effect. Telling the sender their message was
    // refused as automated is free feedback for tuning the next attempt, so the page
    // says what a delivered message says and no mail is sent.
    private static async Task<IResult> SendContactAsync(ContactForm form, HttpContext http)
    {
        ILogger logger = Logger(http, "Contact");
        BotTrapVerdict verdict = BotTraps.Inspect(http, http.Request.Form);

        if (verdict.Tripped)
        {
            BotTraps.LogTripped(logger, verdict, http.Request.Path);
            return Sent(http, form.Email ?? "");
        }

        LocaleCatalog locale = Locales.Get(http.GetLocale());

        if (!IsComplete(form))
        {
            return Contact(
                http,
                Antiforgery(http),
                notice: "",
                error: locale["contact.incomplete"],
                name: form.Name ?? "",
                email: form.Email ?? "",
                message: form.Message ?? "");
        }

        string destination = http.RequestServices.GetRequiredService<ContactDestination>().Address;

        try
        {
            // The sender's address is in the BODY, not the envelope. It is the one
            // piece of this submission an attacker chooses freely, and a public form
            // that puts it in the "to" field is an open relay wearing this domain's
            // reputation. Where it is, a person reading the message can still reply.
            await http.RequestServices.GetRequiredService<IEmailTransport>()
                .SendAsync(
                    destination,
                    new EmailContent(
                        $"Contact from {form.Name}",
                        $"From: {form.Email}\n\n{form.Message}",
                        $"<p>From: {System.Net.WebUtility.HtmlEncode(form.Email)}</p>"
                            + $"<p>{System.Net.WebUtility.HtmlEncode(form.Message)}</p>"),
                    http.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Broad on purpose, and warning rather than error. A transport reports a
            // failed send by throwing and each one throws its own type; what matters
            // to the visitor is the same either way, and a mail server being down is
            // not this application failing. R9: never a 500.
            LogSendFailed(logger, ex);

            return Contact(
                http,
                Antiforgery(http),
                notice: "",
                error: locale["contact.unsent"],
                name: form.Name ?? "",
                email: form.Email ?? "",
                message: form.Message ?? "");
        }

        return Sent(http, form.Email!);
    }

    private static Task<IResult> UploadPageAsync(HttpContext http, IAntiforgery antiforgery) =>
        Task.FromResult(Upload(http, antiforgery, error: "", storedId: "", storedName: "", caption: ""));

    private static async Task<IResult> StorePictureAsync(UploadForm form, HttpContext http)
    {
        ILogger logger = Logger(http, "Upload");
        BotTrapVerdict verdict = BotTraps.Inspect(http, http.Request.Form);

        if (verdict.Tripped)
        {
            BotTraps.LogTripped(logger, verdict, http.Request.Path);
            return Upload(http, Antiforgery(http), error: "", storedId: "", storedName: "", caption: "");
        }

        UploadStore store = http.RequestServices.GetRequiredService<UploadStore>();
        string? stored;

        try
        {
            stored = await store.StoreAsync(form.Picture, http.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Both, because they are the same event to a visitor and only one of them
            // is an IOException: a directory that became read-only between startup's
            // probe and this write raises UnauthorizedAccessException, which used to
            // escape as a 500 for what is an operator's problem, not the client's.
            LogStoreFailed(logger, ex);

            return Upload(
                http,
                Antiforgery(http),
                error: Locales.Get(http.GetLocale())["upload.failed"],
                storedId: "",
                storedName: "",
                caption: form.Caption ?? "");
        }

        if (stored is null)
        {
            LogStoreFull(logger, UploadStore.MaxStoredFiles);

            return Upload(
                http,
                Antiforgery(http),
                error: Locales.Get(http.GetLocale())["upload.full"],
                storedId: "",
                storedName: "",
                caption: form.Caption ?? "");
        }

        // form.Picture.Name is the name the CLIENT sent. It is shown and nothing
        // else: the stored name came from the store, and R13 is that these two are
        // never the same string.
        return Upload(
            http,
            Antiforgery(http),
            error: "",
            storedId: stored,
            storedName: form.Picture.Name,
            caption: form.Caption ?? "");
    }

    private static Task<IResult> ServeUploadAsync(string id, HttpContext http)
    {
        UploadStore store = http.RequestServices.GetRequiredService<UploadStore>();
        string? path = store.Resolve(id, out string contentType);

        return Task.FromResult(path is null ? Results.NotFound() : Results.File(path, contentType));
    }

    private static Task<IResult> SurveyPageAsync(HttpContext http, IAntiforgery antiforgery) =>
        Task.FromResult(SurveyStart(http, antiforgery, error: "", role: ""));

    private static Task<IResult> SurveyStepTwoAsync(SurveyStepOne form, HttpContext http)
    {
        BotTrapVerdict verdict = BotTraps.Inspect(http, http.Request.Form);

        if (verdict.Tripped)
        {
            BotTraps.LogTripped(verdict: verdict, logger: Logger(http, "Survey"), path: http.Request.Path);
            return Task.FromResult(SurveyStart(http, Antiforgery(http), error: "", role: ""));
        }

        LocaleCatalog locale = Locales.Get(http.GetLocale());

        if (string.IsNullOrWhiteSpace(form.Role))
        {
            return Task.FromResult(SurveyStart(http, Antiforgery(http), error: locale["survey.incomplete"], role: ""));
        }

        return Task.FromResult(StepTwo(http, locale, form.Role, error: ""));
    }

    // Step two, rendered with a freshly protected carried value. Freshly on both
    // paths: the lifetime restarts from this render, so a visitor who is answering
    // rather than idling never meets the expiry.
    private static IResult StepTwo(HttpContext http, LocaleCatalog locale, string role, string error)
    {
        CarriedAnswers carried = http.RequestServices.GetRequiredService<CarriedAnswers>();
        AntiforgeryTokenSet tokens = Antiforgery(http).GetAndStoreTokens(http);

        return Templates.Forms.SurveyStepTwo.Result(
            locale: locale,
            Title: locale["survey.step2.title"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            AntiforgeryFieldName: tokens.FormFieldName,
            BotCanary: BotTraps.IssueCanary(http),
            Error: error,
            Role: role,
            Carried: carried.Protect(
                new Dictionary<string, string> { [SurveyPurpose] = role }, CarriedLifetime),
            Comment: "");
    }

    // Three failures, three things to tell a person. Absent used to be folded into
    // "altered", which told a visitor whose form carried no hidden field at all -- a
    // bookmarked step-two URL, a bare re-post -- that their data had been tampered
    // with.
    private static string CarriedMessage(CarriedAnswersFailure failure) =>
        failure switch
        {
            CarriedAnswersFailure.Expired => "survey.expired",
            CarriedAnswersFailure.Absent => "survey.absent",
            _ => "survey.altered",
        };

    private static Task<IResult> SurveyFinishAsync(SurveyStepTwo form, HttpContext http)
    {
        BotTrapVerdict verdict = BotTraps.Inspect(http, http.Request.Form);

        if (verdict.Tripped)
        {
            BotTraps.LogTripped(verdict: verdict, logger: Logger(http, "Survey"), path: http.Request.Path);
            return Task.FromResult(SurveyStart(http, Antiforgery(http), error: "", role: ""));
        }

        LocaleCatalog locale = Locales.Get(http.GetLocale());
        CarriedAnswers carried = http.RequestServices.GetRequiredService<CarriedAnswers>();

        if (!carried.TryUnprotect(form.Carried, out IReadOnlyDictionary<string, string> answers, out CarriedAnswersFailure failure))
        {
            // Two different refusals, which is the whole reason CarriedAnswers checks
            // expiry itself rather than leaving it to the protector: "start again,
            // this took too long" is an instruction and "this was altered" is not.
            return Task.FromResult(SurveyStart(
                http,
                Antiforgery(http),
                error: locale[CarriedMessage(failure)],
                role: ""));
        }

        if (!answers.TryGetValue(SurveyPurpose, out string? role))
        {
            return Task.FromResult(SurveyStart(http, Antiforgery(http), error: locale["survey.incomplete"], role: ""));
        }

        if (string.IsNullOrWhiteSpace(form.Comment))
        {
            // Step TWO again, not step one. The carried value was read back and is
            // still good; throwing the visitor back to the start over a blank second
            // answer would make them retype an answer the server just verified.
            return Task.FromResult(StepTwo(http, locale, role, error: locale["survey.incomplete"]));
        }

        int responses = http.RequestServices.GetRequiredService<SurveyResponses>().Record();

        return Task.FromResult(Templates.Forms.SurveyDone.Result(
            locale: locale,
            Title: locale["survey.done.title"],
            AntiforgeryToken: "",
            Role: role,
            Comment: form.Comment,
            Responses: responses));
    }

    // The address is named, because a confirmation that says only "sent" tells a
    // person nothing they can check. It is the address they typed and the one a reply
    // would go to -- NOT where the message went, which is configuration and is not the
    // visitor's business to be told.
    private static IResult Sent(HttpContext http, string toAddress)
    {
        LocaleCatalog locale = Locales.Get(http.GetLocale());

        return Contact(
            http,
            Antiforgery(http),
            notice: locale["contact.sent"] + " " + toAddress,
            error: "",
            name: "",
            email: "",
            message: "");
    }

    private static IResult Contact(
        HttpContext http,
        IAntiforgery antiforgery,
        string notice,
        string error,
        string name,
        string email,
        string message)
    {
        LocaleCatalog locale = Locales.Get(http.GetLocale());
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        return Templates.Forms.Contact.Result(
            locale: locale,
            Title: locale["contact.title"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            AntiforgeryFieldName: tokens.FormFieldName,
            BotCanary: BotTraps.IssueCanary(http),
            Notice: notice,
            Error: error,
            Name: name,
            Email: email,
            Message: message);
    }

    private static IResult Upload(
        HttpContext http,
        IAntiforgery antiforgery,
        string error,
        string storedId,
        string storedName,
        string caption)
    {
        LocaleCatalog locale = Locales.Get(http.GetLocale());
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        return Templates.Forms.Upload.Result(
            locale: locale,
            Title: locale["upload.title"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            AntiforgeryFieldName: tokens.FormFieldName,
            BotCanary: BotTraps.IssueCanary(http),
            Error: error,
            StoredId: storedId,
            StoredName: storedName,
            Caption: caption);
    }

    private static IResult SurveyStart(HttpContext http, IAntiforgery antiforgery, string error, string role)
    {
        LocaleCatalog locale = Locales.Get(http.GetLocale());
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        return Templates.Forms.Survey.Result(
            locale: locale,
            Title: locale["survey.title"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            AntiforgeryFieldName: tokens.FormFieldName,
            BotCanary: BotTraps.IssueCanary(http),
            Error: error,
            Role: role);
    }

    // Enough to keep an obviously-unsendable address out of the transport, and no
    // more. A form is not the place to decide whether an address exists.
    private static bool IsComplete(ContactForm form) =>
        !string.IsNullOrWhiteSpace(form.Name)
        && !string.IsNullOrWhiteSpace(form.Message)
        && form.Email is { Length: > 2 }
        && form.Email.IndexOf('@', StringComparison.Ordinal) > 0
        && form.Email.IndexOf('@', StringComparison.Ordinal) < form.Email.Length - 1
        && !form.Email.Any(char.IsWhiteSpace);

    private static IAntiforgery Antiforgery(HttpContext http) =>
        http.RequestServices.GetRequiredService<IAntiforgery>();

    private static ILogger Logger(HttpContext http, string category) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger($"NativeWeb.Sample.{category}");

    [LoggerMessage(
        EventId = 2701,
        Level = LogLevel.Warning,
        Message = "Contact form could not send.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2702,
        Level = LogLevel.Warning,
        Message = "Upload could not be stored.")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2703,
        Level = LogLevel.Warning,
        Message = "Upload refused: the store already holds its ceiling of {Ceiling} pictures.")]
    private static partial void LogStoreFull(ILogger logger, int ceiling);
}

/// <summary>
/// How many survey responses this process has taken.
/// </summary>
/// <remarks>
/// In memory and per process, which is all R15 needs: the point of the number is to
/// show a visitor that a second submit was recorded as a second response rather than
/// silently swallowed. Nothing here persists, and nothing should read it as a total.
/// </remarks>
internal sealed class SurveyResponses
{
    private int _count;

    internal int Record() => Interlocked.Increment(ref _count);
}
