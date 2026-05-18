document.addEventListener("DOMContentLoaded", () => {
    const themeStorageKey = "focus-theme";
    const themeToggleButton = document.getElementById("themeToggleButton");
    const documentRoot = document.documentElement;
    const ajaxTargetSelector = "main[role='main']";
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

    document.addEventListener("click", async event => {
        const copyButton = event.target.closest(".js-copy-target");
        if (!(copyButton instanceof HTMLButtonElement)) {
            return;
        }

        const targetId = copyButton.dataset.copyTarget;
        if (!targetId) {
            return;
        }

        const target = document.getElementById(targetId);
        if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement)) {
            return;
        }

        try {
            await navigator.clipboard.writeText(target.value);
        } catch {
            target.focus();
            target.select();
            document.execCommand("copy");
        }
    });

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

    const setSubmittingState = form => {
        const submitButton = form.querySelector("button[type='submit']");
        if (!submitButton) {
            return null;
        }

        submitButton.classList.add("is-submitting");
        submitButton.setAttribute("disabled", "disabled");
        if (!submitButton.dataset.originalText) {
            submitButton.dataset.originalText = submitButton.textContent ?? "";
        }

        submitButton.textContent = form.dataset.ajaxLoadingText || "Saving...";
        return submitButton;
    };
    const clearSubmittingState = submitButton => {
        if (!(submitButton instanceof HTMLButtonElement)) {
            return;
        }

        submitButton.classList.remove("is-submitting");
        submitButton.removeAttribute("disabled");
        submitButton.textContent = submitButton.dataset.originalText || submitButton.textContent || "";
    };
    const reparseValidation = root => {
        const jquery = window.jQuery;
        if (jquery && jquery.validator && jquery.validator.unobtrusive) {
            jquery.validator.unobtrusive.parse(root);
        }
    };
    const initializeDashboardPackLazyLoads = root => {
        const scope = root instanceof Element || root instanceof Document ? root : document;
        const loaders = scope.querySelectorAll(".js-dashboard-pack-loader");
        for (const loader of loaders) {
            if (!(loader instanceof HTMLElement) || loader.dataset.packLoading === "true" || loader.dataset.packLoaded === "true") {
                continue;
            }

            const requestUrl = loader.dataset.dashboardPackUrl;
            const question = loader.dataset.question;
            if (!requestUrl || !question) {
                continue;
            }

            loader.dataset.packLoading = "true";
            const url = new URL(requestUrl, window.location.origin);
            url.search = new URLSearchParams({
                question,
                packGoal: loader.dataset.packGoal || "General",
                resultsPerSection: loader.dataset.resultsPerSection || "6",
                includeCompletedWork: loader.dataset.includeCompletedWork || "false",
                expandHistory: loader.dataset.expandHistory || "true"
            }).toString();

            fetch(url, {
                credentials: "same-origin",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            })
                .then(response => {
                    if (response.status === 204) {
                        return "";
                    }

                    if (!response.ok) {
                        throw new Error(`Request failed with ${response.status}`);
                    }

                    return response.text();
                })
                .then(html => {
                    if (!html.trim()) {
                        loader.remove();
                        return;
                    }

                    const parsed = new DOMParser().parseFromString(html, "text/html");
                    const replacementNodes = Array.from(parsed.body.childNodes);
                    if (replacementNodes.length === 0) {
                        loader.remove();
                        return;
                    }

                    const fragment = document.createDocumentFragment();
                    for (const node of replacementNodes) {
                        fragment.appendChild(node);
                    }

                    loader.replaceWith(fragment);
                    initializeDashboardPackLazyLoads(document);
                })
                .catch(() => {
                    loader.dataset.packLoading = "false";
                    loader.classList.add("dashboard-pack-loader-error");
                    const title = loader.querySelector("h2");
                    const copy = loader.querySelector(".memory-summary");
                    if (title) {
                        title.textContent = "Context pack is taking longer than usual";
                    }
                    if (copy) {
                        copy.textContent = "Refresh the page or build the pack manually if the loader stays here.";
                    }
                })
                .finally(() => {
                    loader.dataset.packLoaded = "true";
                });
        }
    };
    const swapAjaxTarget = async (requestUrl, requestInit, targetSelector) => {
        const response = await fetch(requestUrl, {
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            },
            ...requestInit
        });
        if (!response.ok) {
            throw new Error(`Request failed with ${response.status}`);
        }

        const html = await response.text();
        const parsed = new DOMParser().parseFromString(html, "text/html");
        const currentTarget = document.querySelector(targetSelector);
        const nextTarget = parsed.querySelector(targetSelector);
        if (!currentTarget || !nextTarget) {
            window.location.assign(response.url || requestUrl.toString());
            return;
        }

        currentTarget.replaceWith(nextTarget);
        document.title = parsed.title || document.title;
        if ((response.url || requestUrl.toString()) !== window.location.href) {
            window.history.pushState({}, "", response.url || requestUrl.toString());
        }

        reparseValidation(nextTarget);
        initializeDashboardPackLazyLoads(nextTarget);
    };

    document.addEventListener("submit", async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        const jquery = window.jQuery;
        if (jquery && jquery.validator && !jquery(form).valid()) {
            return;
        }

        if (!form.checkValidity()) {
            return;
        }

        const submitButton = setSubmittingState(form);
        const targetSelector = form.dataset.ajaxTarget;
        if (!targetSelector) {
            return;
        }

        event.preventDefault();

        try {
            const method = (form.method || "get").toUpperCase();
            const formData = new FormData(form);
            if (method === "GET") {
                const url = new URL(form.action || window.location.href, window.location.origin);
                url.search = new URLSearchParams(formData).toString();
                await swapAjaxTarget(url, { method }, targetSelector);
                return;
            }

            await swapAjaxTarget(
                new URL(form.action || window.location.href, window.location.origin),
                {
                    method,
                    body: formData
                },
                targetSelector);
        } catch {
            clearSubmittingState(submitButton);
            window.location.assign(form.action || window.location.href);
        }
    });

    document.addEventListener("click", async event => {
        const link = event.target.closest("a[data-ajax-target]");
        if (!(link instanceof HTMLAnchorElement)) {
            return;
        }

        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || link.target === "_blank") {
            return;
        }

        event.preventDefault();
        try {
            await swapAjaxTarget(new URL(link.href, window.location.origin), { method: "GET" }, link.dataset.ajaxTarget || ajaxTargetSelector);
        } catch {
            window.location.assign(link.href);
        }
    });

    window.addEventListener("popstate", async () => {
        const ajaxLinks = document.querySelector("a[data-ajax-target], form[data-ajax-target]");
        if (!ajaxLinks) {
            return;
        }

        try {
            await swapAjaxTarget(new URL(window.location.href), { method: "GET" }, ajaxTargetSelector);
        } catch {
            window.location.reload();
        }
    });

    initializeDashboardPackLazyLoads(document);
});
