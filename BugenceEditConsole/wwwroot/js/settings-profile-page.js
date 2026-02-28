(() => {
  if (!document.body.classList.contains("page-settings-profile-v3")) return;

  const token = document.querySelector('#profileAntiForgery input[name="__RequestVerificationToken"]')?.value || "";
  const toast = document.getElementById("profileToast");
  const toastTitle = document.getElementById("profileToastTitle");
  const toastBody = document.getElementById("profileToastBody");
  const avatarInput = document.getElementById("avatarInput");
  const uploadAvatarBtn = document.getElementById("uploadAvatarBtn");
  const companyLogoInput = document.getElementById("companyLogoInput");
  const uploadCompanyLogoBtn = document.getElementById("uploadCompanyLogoBtn");
  const recoveryCodesPanel = document.getElementById("recoveryCodesPanel");
  const recoveryCodesGrid = document.getElementById("recoveryCodesGrid");
  const recoveryCodesLeft = document.getElementById("recoveryCodesLeft");
  const recoveryCodesCountLabel = document.getElementById("recoveryCodesCountLabel");

  const showToast = (title, body) => {
    if (!toast) return;
    toastTitle.textContent = title || "Updated";
    toastBody.textContent = body || "";
    toast.classList.add("show");
    clearTimeout(showToast._timer);
    showToast._timer = setTimeout(() => toast.classList.remove("show"), 3600);
  };

  const jsonPost = async (url, payload) => {
    const response = await fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        RequestVerificationToken: token
      },
      body: JSON.stringify(payload || {})
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data?.success !== true) throw new Error(data?.message || "Request failed.");
    return data;
  };

  const formPost = async (url, formData) => {
    const response = await fetch(url, { method: "POST", credentials: "same-origin", body: formData });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data?.success !== true) throw new Error(data?.message || "Request failed.");
    return data;
  };

  const setAvatarEverywhere = (url, initials) => {
    if (!url) return;
    try { localStorage.setItem("bugence.account.avatar", url); } catch {}
    document.querySelectorAll(".dash-avatar-dot, .avatar-dot, .profile-avatar, .ph-avatar-dot").forEach((el) => {
      if (!(el instanceof HTMLElement)) return;
      el.style.backgroundImage = `url("${url}")`;
      el.style.backgroundSize = "cover";
      el.style.backgroundPosition = "center";
      el.textContent = "";
    });
    const avatarStage = document.getElementById("profileAvatarStage");
    if (avatarStage instanceof HTMLElement) avatarStage.innerHTML = `<img src="${url}" alt="${initials || "Avatar"}" />`;
  };

  const syncProfileText = (displayName, email, initials) => {
    document.getElementById("profileDisplayName").textContent = displayName;
    document.getElementById("profileDisplayEmail").textContent = email;
    document.querySelectorAll("[data-profile-name]").forEach((el) => (el.textContent = displayName));
    document.querySelectorAll("[data-profile-email]").forEach((el) => (el.textContent = email));
    document.querySelectorAll("[data-profile-initials]").forEach((el) => {
      if (!(el instanceof HTMLElement)) return;
      if (!el.style.backgroundImage) el.textContent = initials;
    });
  };

  const syncCompanyLogo = (url) => {
    const stage = document.getElementById("companyLogoStage");
    if (stage instanceof HTMLElement && url) stage.innerHTML = `<img src="${url}" alt="Company logo" />`;
  };

  const renderRecoveryCodes = (codes) => {
    const list = Array.isArray(codes) ? codes.filter(Boolean) : [];
    if (!recoveryCodesPanel || !recoveryCodesGrid) return;
    if (!list.length) {
      recoveryCodesPanel.style.display = "none";
      recoveryCodesGrid.innerHTML = "";
      return;
    }
    recoveryCodesGrid.innerHTML = list.map((code) => `<code>${code}</code>`).join("");
    recoveryCodesPanel.style.display = "block";
    if (recoveryCodesLeft) recoveryCodesLeft.textContent = String(list.length);
    if (recoveryCodesCountLabel) recoveryCodesCountLabel.textContent = String(list.length);
  };

  const syncTwoFactorUi = (enabled) => {
    document.getElementById("twoFactorMetric").textContent = enabled ? "Enabled" : "Off";
    document.getElementById("twoFactorStatusText").textContent = enabled ? "Enabled" : "Not Enabled";
    document.getElementById("disableTwoFactorBtn").disabled = !enabled;
    document.getElementById("twoFactorStatusChip").classList.toggle("connected", !!enabled);
    document.getElementById("twoFactorStatusChip").classList.toggle("pending", !enabled);
  };

  document.querySelectorAll("[data-security-tab]").forEach((button) =>
    button.addEventListener("click", () => {
      const tab = button.getAttribute("data-security-tab");
      document.querySelectorAll("[data-security-tab]").forEach((el) => el.classList.toggle("active", el === button));
      document.querySelectorAll("[data-security-pane]").forEach((pane) => pane.classList.toggle("active", pane.getAttribute("data-security-pane") === tab));
    })
  );

  document.getElementById("saveProfileBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("saveProfileBtn");
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=Save", {
        name: document.getElementById("nameInput").value.trim(),
        email: document.getElementById("emailInput").value.trim(),
        phone: document.getElementById("phoneInput").value.trim(),
        country: document.getElementById("countryInput").value.trim(),
        timezone: document.getElementById("timezoneInput").value.trim()
      });
      document.getElementById("nameChangesRemaining").textContent = data.nameChangesRemaining ?? 0;
      document.getElementById("nameChangeLabel").textContent = data.nameChangesRemaining ?? 0;
      syncProfileText(data.displayName || "", data.email || "", data.initials || "AD");
      if (data.avatarUrl) setAvatarEverywhere(data.avatarUrl, data.initials || "AD");
      showToast("Profile Saved", "Identity updates are now reflected across the workspace.");
    } catch (error) {
      showToast("Save Failed", error?.message || "Unable to save profile.");
    } finally {
      button.disabled = false;
    }
  });

  uploadAvatarBtn?.addEventListener("click", () => avatarInput?.click());
  avatarInput?.addEventListener("change", async () => {
    const file = avatarInput.files?.[0];
    if (!file) return;
    uploadAvatarBtn.disabled = true;
    try {
      const form = new FormData();
      form.append("__RequestVerificationToken", token);
      form.append("avatar", file);
      const data = await formPost("/Settings/Profile?handler=Avatar", form);
      setAvatarEverywhere(data.avatarUrl, data.initials || "AD");
      syncProfileText(data.displayName || document.getElementById("nameInput").value.trim(), document.getElementById("emailInput").value.trim(), data.initials || "AD");
      showToast("Avatar Updated", "Your new profile image is active across Bugence.");
    } catch (error) {
      showToast("Upload Failed", error?.message || "Unable to upload avatar.");
    } finally {
      uploadAvatarBtn.disabled = false;
      avatarInput.value = "";
    }
  });

  document.getElementById("saveCompanyBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("saveCompanyBtn");
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=CompanySave", {
        companyName: document.getElementById("companyNameInput").value.trim(),
        addressLine1: document.getElementById("companyAddress1Input").value.trim(),
        addressLine2: document.getElementById("companyAddress2Input").value.trim(),
        city: document.getElementById("companyCityInput").value.trim(),
        stateOrProvince: document.getElementById("companyStateInput").value.trim(),
        postalCode: document.getElementById("companyPostalInput").value.trim(),
        country: document.getElementById("companyCountryInput").value.trim(),
        phoneNumber: document.getElementById("companyPhoneInput").value.trim(),
        expectedUserCount: document.getElementById("companySeatsInput").value ? Number(document.getElementById("companySeatsInput").value) : null
      });
      document.getElementById("companyNameHeadline").textContent = data.company?.name || "Company updated";
      showToast("Company Saved", "Workspace company details have been updated.");
    } catch (error) {
      showToast("Company Save Failed", error?.message || "Unable to save company details.");
    } finally {
      button.disabled = false;
    }
  });

  uploadCompanyLogoBtn?.addEventListener("click", () => companyLogoInput?.click());
  companyLogoInput?.addEventListener("change", async () => {
    const file = companyLogoInput.files?.[0];
    if (!file) return;
    uploadCompanyLogoBtn.disabled = true;
    try {
      const form = new FormData();
      form.append("__RequestVerificationToken", token);
      form.append("logo", file);
      const data = await formPost("/Settings/Profile?handler=CompanyLogo", form);
      syncCompanyLogo(data.logoUrl);
      showToast("Company Logo Updated", "Company branding is live across your workspace.");
    } catch (error) {
      showToast("Logo Upload Failed", error?.message || "Unable to upload company logo.");
    } finally {
      uploadCompanyLogoBtn.disabled = false;
      companyLogoInput.value = "";
    }
  });

  document.getElementById("changePasswordBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("changePasswordBtn");
    button.disabled = true;
    try {
      await jsonPost("/Settings/Profile?handler=ChangePassword", {
        currentPassword: document.getElementById("currentPasswordInput").value,
        newPassword: document.getElementById("newPasswordInput").value,
        confirmPassword: document.getElementById("confirmPasswordInput").value
      });
      document.getElementById("currentPasswordInput").value = "";
      document.getElementById("newPasswordInput").value = "";
      document.getElementById("confirmPasswordInput").value = "";
      showToast("Password Updated", "Your account password has been changed.");
    } catch (error) {
      showToast("Password Update Failed", error?.message || "Unable to update password.");
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById("copySharedKeyBtn")?.addEventListener("click", async () => {
    const key = document.getElementById("twoFactorSharedKey")?.textContent?.trim() || "";
    try {
      await navigator.clipboard.writeText(key);
      showToast("Setup Key Copied", "Authenticator setup key copied to the clipboard.");
    } catch {
      showToast("Copy Failed", "Unable to copy the setup key.");
    }
  });

  document.getElementById("refreshTwoFactorBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("refreshTwoFactorBtn");
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=RefreshTwoFactor", {});
      document.getElementById("twoFactorSharedKey").textContent = data.sharedKey || "";
      const qr = document.getElementById("twoFactorQrImage");
      if (qr instanceof HTMLImageElement && data.authenticatorUri) qr.src = `https://quickchart.io/qr?size=220&margin=1&text=${encodeURIComponent(data.authenticatorUri)}`;
      syncTwoFactorUi(false);
      renderRecoveryCodes([]);
      showToast("Authenticator Reset", data.message || "A new authenticator key has been generated.");
    } catch (error) {
      showToast("Reset Failed", error?.message || "Unable to reset the authenticator key.");
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById("enableTwoFactorBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("enableTwoFactorBtn");
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=EnableTwoFactor", {
        code: document.getElementById("twoFactorCodeInput").value.trim()
      });
      document.getElementById("twoFactorCodeInput").value = "";
      syncTwoFactorUi(true);
      renderRecoveryCodes(data.recoveryCodes);
      showToast("Two-Factor Enabled", data.message || "Two-factor authentication is now enabled.");
    } catch (error) {
      showToast("Enable Failed", error?.message || "Unable to enable two-factor authentication.");
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById("disableTwoFactorBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("disableTwoFactorBtn");
    if (button.disabled) return;
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=DisableTwoFactor", {});
      syncTwoFactorUi(false);
      renderRecoveryCodes([]);
      showToast("Two-Factor Disabled", data.message || "Two-factor authentication has been disabled.");
    } catch (error) {
      showToast("Disable Failed", error?.message || "Unable to disable two-factor authentication.");
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById("generateRecoveryCodesBtn")?.addEventListener("click", async () => {
    const button = document.getElementById("generateRecoveryCodesBtn");
    button.disabled = true;
    try {
      const data = await jsonPost("/Settings/Profile?handler=RecoveryCodes", {});
      renderRecoveryCodes(data.recoveryCodes);
      showToast("Recovery Codes Generated", data.message || "New recovery codes have been generated.");
    } catch (error) {
      showToast("Recovery Code Error", error?.message || "Unable to generate recovery codes.");
    } finally {
      button.disabled = false;
    }
  });
})();
