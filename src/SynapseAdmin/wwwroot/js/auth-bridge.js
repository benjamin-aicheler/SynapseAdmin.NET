// Completes the Blazor -> cookie session handoff (see AuthController.SignIn).
// The bridge key is sent in the request body (never in a URL), together with this browser's
// antiforgery request token, which the bridge is bound to.
export async function completeSignIn(key, requestToken, headerName) {
    const response = await fetch('/Auth/SignIn', {
        method: 'POST',
        credentials: 'same-origin',
        cache: 'no-store',
        headers: {
            'Content-Type': 'application/json',
            [headerName]: requestToken
        },
        body: JSON.stringify({ key })
    });
    return response.ok;
}
