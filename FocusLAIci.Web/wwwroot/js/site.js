document.addEventListener("DOMContentLoaded", () => {
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
        form.addEventListener("submit", () => {
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
