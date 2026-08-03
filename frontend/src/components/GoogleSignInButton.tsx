import { GoogleOAuthProvider, useGoogleLogin } from "@react-oauth/google";

type GoogleUser = {
  name: string;
  email: string;
  picture?: string;
};

type GoogleSignInButtonProps = {
  disabled?: boolean;
  onStart?: () => void;
  onSuccess: (user: GoogleUser) => void;
  onError: (message: string) => void;
};

const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID ?? "";

const GoogleIcon = () => (
  <span className="btn-google__icon" aria-hidden="true">
    <svg viewBox="0 0 24 24" width="18" height="18">
      <path
        fill="#4285F4"
        d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"
      />
      <path
        fill="#34A853"
        d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
      />
      <path
        fill="#FBBC05"
        d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
      />
      <path
        fill="#EA4335"
        d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
      />
    </svg>
  </span>
);

const GoogleSignInButtonConfigured = ({
  disabled = false,
  onStart,
  onSuccess,
  onError,
}: GoogleSignInButtonProps) => {
  const login = useGoogleLogin({
    onSuccess: async (tokenResponse) => {
      try {
        const response = await fetch("https://www.googleapis.com/oauth2/v3/userinfo", {
          headers: {
            Authorization: `Bearer ${tokenResponse.access_token}`,
          },
        });

        if (!response.ok) {
          throw new Error("Failed to fetch Google profile.");
        }

        const profile = (await response.json()) as {
          name?: string;
          email?: string;
          picture?: string;
        };

        if (!profile.email) {
          throw new Error("Google account email is unavailable.");
        }

        onSuccess({
          name: profile.name ?? profile.email,
          email: profile.email,
          picture: profile.picture,
        });
      } catch {
        onError("Google sign-in failed. Please try again.");
      }
    },
    onError: () => {
      onError("Google sign-in was cancelled or failed.");
    },
    onNonOAuthError: () => {
      onError("Google sign-in is unavailable right now.");
    },
  });

  return (
    <button
      type="button"
      className="btn btn-google w-100 text-nav"
      onClick={() => {
        if (disabled) return;
        onStart?.();
        login();
      }}
      disabled={disabled}
      aria-label="Continue with Google"
    >
      <GoogleIcon />
      Continue with Google
    </button>
  );
};

const GoogleSignInButton = (props: GoogleSignInButtonProps) => {
  if (!googleClientId) {
    return (
      <button
        type="button"
        className="btn btn-google w-100 text-nav"
        onClick={() => {
          if (props.disabled) return;
          props.onError("Google sign-in is not configured yet.");
        }}
        disabled={props.disabled}
        aria-label="Continue with Google"
      >
        <GoogleIcon />
        Continue with Google
      </button>
    );
  }

  return (
    <GoogleOAuthProvider clientId={googleClientId}>
      <GoogleSignInButtonConfigured {...props} />
    </GoogleOAuthProvider>
  );
};

export default GoogleSignInButton;
