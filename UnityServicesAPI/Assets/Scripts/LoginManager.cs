using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;

public class LoginManager : MonoBehaviour
{
    bool isInitializing;
    bool initialized;
    public bool IsLoggedIn => AuthenticationService.Instance?.IsSignedIn == true;
    [SerializeField] bool autoRestoreOnStart = true;

    private async void Start()
    {
        await EnsureInitializedAsync();
        if (autoRestoreOnStart)
        {
            await TryRestoreCachedSessionAsync();
        }
        Debug.Log($"Auth state on start: {(AuthenticationService.Instance.IsSignedIn ? "Signed in" : "Signed out")}");
    }

    public async void SignInAsGuestAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"Already signed in. PlayerID: {AuthenticationService.Instance.PlayerId}");
                return;
            }
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"Signed in anonymously. PlayerID: {AuthenticationService.Instance.PlayerId}");
            UpdateStatusLog();
        }
        catch (RequestFailedException ex)
        {
            Debug.LogError($"Anonymous sign-in failed: {ex.ErrorCode} {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Anonymous sign-in error: {ex.Message}");
        }
    }

    public async void SignInWithUnityAccountAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (!IsPlayerAccountsAvailable())
            {
                Debug.LogWarning("Player Accounts not configured. Enable it in Project Settings → Services → Player Accounts.");
                return;
            }
            var token = await EnsureUpaSignedInAndGetAccessTokenAsync();
            await AuthenticationService.Instance.SignInWithUnityAsync(token);
            Debug.Log($"Signed in with Unity. PlayerID: {AuthenticationService.Instance.PlayerId}");
            UpdateStatusLog();
        }
        catch (RequestFailedException ex)
        {
            Debug.LogError($"Unity sign-in failed: {ex.ErrorCode} {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unity sign-in error: {ex.Message}");
        }
    }

    public async void LinkUnityAccountOnlyAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.LogWarning("Link requires being signed in (e.g., as guest).");
                return;
            }
            if (!IsPlayerAccountsAvailable())
            {
                Debug.LogWarning("Player Accounts not configured. Enable it in Project Settings → Services → Player Accounts.");
                return;
            }
            var token = await EnsureUpaSignedInAndGetAccessTokenAsync();
            await AuthenticationService.Instance.LinkWithUnityAsync(token);
            Debug.Log($"Linked Unity account. PlayerID: {AuthenticationService.Instance.PlayerId}");
            UpdateStatusLog();
        }
        catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            Debug.LogWarning("Account already linked.");
        }
        catch (RequestFailedException ex)
        {
            Debug.LogError($"Unity link failed: {ex.ErrorCode} {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unity link error: {ex.Message}");
        }
    }

    public async void SignOutAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut();
                Debug.Log("Signed out from Authentication.");
            }
            else
            {
                Debug.Log("Already signed out from Authentication.");
            }
            if (IsPlayerAccountsAvailable() && PlayerAccountService.Instance.IsSignedIn)
            {
                PlayerAccountService.Instance.SignOut();
                Debug.Log("Signed out from Player Accounts.");
            }
            AuthenticationService.Instance.ClearSessionToken();
            Debug.Log("Session token cleared.");
            UpdateStatusLog();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Sign out error: {ex.Message}");
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (initialized) return;
        if (isInitializing)
        {
            while (isInitializing) await Task.Yield();
            return;
        }
        isInitializing = true;
        try
        {
            var state = UnityServices.State;
            if (state == ServicesInitializationState.Uninitialized)
            {
                await UnityServices.InitializeAsync();
            }
            while (UnityServices.State == ServicesInitializationState.Initializing)
            {
                await Task.Yield();
            }
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                throw new InvalidOperationException($"Unity Services not initialized. State: {UnityServices.State}");
            }
            AuthenticationService.Instance.SignedIn += OnSignedIn;
            AuthenticationService.Instance.SignedOut += OnSignedOut;
            AuthenticationService.Instance.Expired += OnExpired;
            AuthenticationService.Instance.SignInFailed += OnSignInFailed;
            initialized = true;
            Debug.Log("Unity Services initialized.");
        }
        finally
        {
            isInitializing = false;
        }
    }

    private async Task TryRestoreCachedSessionAsync()
    {
        try
        {
            if (AuthenticationService.Instance.IsSignedIn) return;
            if (!AuthenticationService.Instance.SessionTokenExists) return;
            await AuthenticationService.Instance.SignInAnonymouslyAsync(new SignInOptions { CreateAccount = false });
            Debug.Log($"Session restored. PlayerID: {AuthenticationService.Instance.PlayerId}");
            UpdateStatusLog();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Session restore failed: {e.Message}");
        }
    }

    private void OnSignedIn()
    {
        Debug.Log($"Signed In. PlayerID: {AuthenticationService.Instance.PlayerId}");
    }

    private void OnSignedOut()
    {
        Debug.Log("Signed Out.");
    }

    private void OnExpired()
    {
        Debug.LogWarning("Session expired.");
    }

    private void OnSignInFailed(RequestFailedException e)
    {
        Debug.LogError($"Sign-in failed: {e.ErrorCode} {e.Message}");
    }

    private void UpdateStatusLog()
    {
        var linked = AuthenticationService.Instance.PlayerInfo?.Identities;
        var info = "";
        if (linked != null)
        {
            foreach (var id in linked)
            {
                info += $"\n - {id.TypeId}: {id.UserId}";
            }
        }
        Debug.Log($"IsLoggedIn={IsLoggedIn}{(string.IsNullOrEmpty(info) ? string.Empty : " Linked:" + info)}");
    }

    private bool IsPlayerAccountsAvailable()
    {
        try
        {
            var _ = PlayerAccountService.Instance;
            return true;
        }
        catch (ServicesInitializationException e)
        {
            Debug.LogWarning($"Player Accounts unavailable: {e.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Player Accounts access error: {e.Message}");
            return false;
        }
    }

    private async Task<string> EnsureUpaSignedInAndGetAccessTokenAsync(int timeoutMs = 120000)
    {
        if (PlayerAccountService.Instance.IsSignedIn && !string.IsNullOrEmpty(PlayerAccountService.Instance.AccessToken))
        {
            return PlayerAccountService.Instance.AccessToken;
        }

        var tcs = new TaskCompletionSource<string>();
        void Handler()
        {
            if (!string.IsNullOrEmpty(PlayerAccountService.Instance.AccessToken))
            {
                tcs.TrySetResult(PlayerAccountService.Instance.AccessToken);
            }
        }

        PlayerAccountService.Instance.SignedIn += Handler;
        try
        {
            await PlayerAccountService.Instance.StartSignInAsync();
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed != tcs.Task)
            {
                throw new TimeoutException("Timed out waiting for Unity Player Accounts token.");
            }
            return await tcs.Task;
        }
        finally
        {
            PlayerAccountService.Instance.SignedIn -= Handler;
        }
    }
}
