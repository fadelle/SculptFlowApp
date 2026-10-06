namespace PlasticSurgery.Entities.Requests.Channels;

/// <summary>
/// Sent by the browser after Facebook Login completes (Messenger use case). Uses the
/// fb:login-button plugin + FB.getLoginStatus(), which hands back an AccessToken directly
/// (implicit flow) rather than a Code — exactly one of the two should be set. Code is kept as an
/// option too in case a future caller uses the FB.login()/response_type:'code' pattern instead.
/// </summary>
public record ConnectFacebookRequest(
    Guid ClinicId,
    string? Code,
    string? AccessToken
);
