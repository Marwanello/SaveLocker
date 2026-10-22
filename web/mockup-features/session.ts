// Signed in as admin, as the console usually is: the mock server has a password, and this tab holds a session.
// Its own module, imported first: api.ts reads the session once, when it loads.
try { sessionStorage.setItem('sl_session', 'mock-session') } catch { /* storage off: the sign-in screen shows */ }
