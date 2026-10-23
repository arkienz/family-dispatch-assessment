// Page behaviour lives in this file, not in inline scripts, so the Content-Security-Policy can be script-src 'self'.

// Login page: a "Sign in as" button fills in that demo account and submits the form.
const loginForm = document.getElementById("login-form");
document.querySelectorAll("button[data-email]").forEach(b => b.addEventListener("click", () => {
    loginForm.querySelector("[name=Email]").value = b.dataset.email;
    loginForm.querySelector("[name=Password]").value = loginForm.dataset.demoPassword;
    loginForm.requestSubmit();
}));

// Load page: a file over the server's request cap is cut off before the page handler can explain why,
// so say it here first. The server still enforces the limit.
const uploadForm = document.getElementById("upload-form");
uploadForm?.addEventListener("submit", e => {
    const file = document.getElementById("file").files[0];
    const tooBig = file && file.size > Number(uploadForm.dataset.maxBytes);
    document.getElementById("file-too-big").hidden = !tooBig;
    if (tooBig) e.preventDefault();
});
