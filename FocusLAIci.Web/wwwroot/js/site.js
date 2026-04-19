document.addEventListener("DOMContentLoaded", () => {
    const themeStorageKey = "focus-theme";
    const themeToggleButton = document.getElementById("themeToggleButton");
    const documentRoot = document.documentElement;
    const applyTheme = theme => {
        documentRoot.setAttribute("data-bs-theme", theme);
        document.body.dataset.theme = theme;

        if (themeToggleButton instanceof HTMLButtonElement) {
            const nextThemeLabel = theme === "dark" ? "Enable light mode" : "Enable dark mode";
            themeToggleButton.setAttribute("aria-label", nextThemeLabel);
            themeToggleButton.setAttribute("title", nextThemeLabel);
            themeToggleButton.setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
        }
    };
    const loadThemePreference = () => {
        try {
            const storedTheme = window.localStorage.getItem(themeStorageKey);
            if (storedTheme === "dark" || storedTheme === "light") {
                return storedTheme;
            }
        } catch {
        }

        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    };
    const persistThemePreference = theme => {
        try {
            window.localStorage.setItem(themeStorageKey, theme);
        } catch {
        }
    };

    applyTheme(loadThemePreference());
    if (themeToggleButton instanceof HTMLButtonElement) {
        themeToggleButton.addEventListener("click", () => {
            const nextTheme = document.body.dataset.theme === "dark" ? "light" : "dark";
            persistThemePreference(nextTheme);
            applyTheme(nextTheme);
        });
    }

    const wingFilter = document.querySelector("select[name='wingId']");
    const roomFilter = document.getElementById("roomFilter");
    if (wingFilter instanceof HTMLSelectElement && roomFilter instanceof HTMLSelectElement) {
        const syncRoomState = () => {
            const hasWing = wingFilter.value.trim().length > 0;
            roomFilter.disabled = !hasWing;
            if (!hasWing) {
                roomFilter.value = "";
            }
        };

        syncRoomState();
        wingFilter.addEventListener("change", syncRoomState);
    }

    document.querySelectorAll("form").forEach(form => {
        form.addEventListener("submit", event => {
            const jquery = window.jQuery;
            if (jquery && jquery.validator && !jquery(form).valid()) {
                return;
            }

            if (!form.checkValidity()) {
                return;
            }

            const submitButton = form.querySelector("button[type='submit']");
            if (!submitButton) {
                return;
            }

            submitButton.classList.add("is-submitting");
            submitButton.setAttribute("disabled", "disabled");
            if (!submitButton.dataset.originalText) {
                submitButton.dataset.originalText = submitButton.textContent ?? "";
            }

            submitButton.textContent = "Saving...";
        });
    });
});
