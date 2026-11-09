(function () {
    "use strict";

    const page = document.querySelector("[data-prescriptions-page]");
    if (!page) {
        return;
    }

    const apiUrl = "/api/prescriptions/images";
    const uploadForm = document.getElementById("prescription-upload-form");
    const fileInput = document.getElementById("prescription-file");
    const list = document.getElementById("prescription-list");
    const count = document.getElementById("prescription-count");
    const status = document.getElementById("prescription-status");
    const error = document.getElementById("prescription-error");
    const viewer = document.getElementById("prescription-viewer");
    const viewerImage = document.getElementById("prescription-viewer-image");
    const viewerName = document.getElementById("prescription-viewer-name");
    let viewerUrl;

    uploadForm.addEventListener("submit", async function (event) {
        event.preventDefault();
        clearMessage();

        const file = fileInput.files[0];
        if (!file) {
            showError("Vui lòng chọn một ảnh.");
            return;
        }

        if (file.size > 5 * 1024 * 1024) {
            showError("Ảnh phải có kích thước không quá 5 MB.");
            return;
        }

        const formData = new FormData();
        formData.append("file", file, file.name);
        try {
            const response = await fetch(apiUrl, {
                method: "POST",
                body: formData,
                headers: csrfHeaders(),
                credentials: "same-origin"
            });
            if (!response.ok) {
                throw new Error(await problemMessage(response));
            }

            fileInput.value = "";
            showStatus("Đã tải ảnh lên.");
            await loadImages();
        } catch (exception) {
            showError(exception.message || "Không thể tải ảnh lên.");
        }
    });

    document.addEventListener("click", async function (event) {
        const button = event.target.closest("[data-image-action]");
        if (button) {
            const imageId = button.dataset.imageId;
            const action = button.dataset.imageAction;
            if (action === "view") {
                await viewImage(imageId, button.dataset.imageName);
            } else if (action === "download") {
                await downloadImage(imageId, button.dataset.imageName);
            } else if (action === "delete") {
                await deleteImage(imageId, button.dataset.imageName);
            }
            return;
        }

        if (event.target.matches("[data-close-viewer]") || event.target === viewer) {
            closeViewer();
        }
    });

    viewer.addEventListener("close", closeViewer);
    loadImages();

    async function loadImages() {
        clearMessage();
        try {
            const response = await fetch(apiUrl, { credentials: "same-origin" });
            if (!response.ok) {
                throw new Error(await problemMessage(response));
            }

            const payload = await response.json();
            renderImages(payload.items || []);
        } catch (exception) {
            list.replaceChildren();
            showError(exception.message || "Không thể tải danh sách ảnh.");
        }
    }

    function renderImages(images) {
        list.replaceChildren();
        count.textContent = images.length === 0 ? "" : `${images.length} ảnh`;
        if (images.length === 0) {
            const empty = document.createElement("p");
            empty.className = "empty-state";
            empty.textContent = "Chưa có ảnh đơn thuốc.";
            list.append(empty);
            return;
        }

        for (const image of images) {
            const card = document.createElement("article");
            card.className = "prescription-card";
            card.append(createPreview(image));

            const details = document.createElement("div");
            details.className = "prescription-card-details";
            const name = document.createElement("strong");
            name.textContent = image.originalFileName;
            const metadata = document.createElement("small");
            metadata.className = "hint";
            metadata.textContent = `${formatBytes(image.sizeBytes)} · ${formatDate(image.uploadedAt)}`;
            details.append(name, metadata);

            const actions = document.createElement("div");
            actions.className = "prescription-actions";
            actions.append(
                actionButton("Xem", "view", image),
                actionButton("Tải xuống", "download", image),
                actionButton("Xóa", "delete", image, "danger-button")
            );
            details.append(actions);
            card.append(details);
            list.append(card);
        }
    }

    function createPreview(image) {
        const preview = document.createElement("button");
        preview.type = "button";
        preview.className = "prescription-preview";
        preview.dataset.imageAction = "view";
        preview.dataset.imageId = image.id;
        preview.dataset.imageName = image.originalFileName;
        preview.setAttribute("aria-label", `Xem ${image.originalFileName}`);
        preview.textContent = image.contentType === "application/pdf" ? "PDF" : "Ảnh";
        return preview;
    }

    function actionButton(label, action, image, className) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className || "secondary-button";
        button.dataset.imageAction = action;
        button.dataset.imageId = image.id;
        button.dataset.imageName = image.originalFileName;
        button.textContent = label;
        return button;
    }

    async function viewImage(imageId, fileName) {
        clearMessage();
        try {
            const response = await fetch(`${apiUrl}/${imageId}/content`, { credentials: "same-origin" });
            if (!response.ok) {
                throw new Error(await problemMessage(response));
            }

            replaceViewerUrl(URL.createObjectURL(await response.blob()));
            viewerImage.alt = fileName;
            viewerName.textContent = fileName;
            viewer.showModal();
        } catch (exception) {
            showError(exception.message || "Không thể mở ảnh.");
        }
    }

    async function downloadImage(imageId, fileName) {
        clearMessage();
        try {
            const response = await fetch(`${apiUrl}/${imageId}/content`, { credentials: "same-origin" });
            if (!response.ok) {
                throw new Error(await problemMessage(response));
            }

            const url = URL.createObjectURL(await response.blob());
            const link = document.createElement("a");
            link.href = url;
            link.download = fileName;
            link.click();
            URL.revokeObjectURL(url);
        } catch (exception) {
            showError(exception.message || "Không thể tải ảnh xuống.");
        }
    }

    async function deleteImage(imageId, fileName) {
        if (!window.confirm(`Xóa ảnh "${fileName}"?`)) {
            return;
        }

        clearMessage();
        try {
            const response = await fetch(`${apiUrl}/${imageId}`, {
                method: "DELETE",
                headers: csrfHeaders(),
                credentials: "same-origin"
            });
            if (!response.ok) {
                throw new Error(await problemMessage(response));
            }

            showStatus("Đã xóa ảnh.");
            await loadImages();
        } catch (exception) {
            showError(exception.message || "Không thể xóa ảnh.");
        }
    }

    function csrfHeaders() {
        const token = uploadForm.querySelector('input[name="__RequestVerificationToken"]').value;
        return { "X-CSRF-TOKEN": token };
    }

    async function problemMessage(response) {
        try {
            const problem = await response.json();
            return problem.code === "FILE_TOO_LARGE"
                ? "Ảnh phải có kích thước không quá 5 MB."
                : problem.code === "FILE_TYPE_NOT_ALLOWED"
                    ? "Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP."
                    : problem.code === "FORBIDDEN"
                        ? "Bạn không có quyền thao tác với ảnh đơn thuốc."
                        : problem.code === "CSRF_INVALID"
                            ? "Phiên thao tác đã hết hạn. Vui lòng tải lại trang."
                            : "Không thể hoàn tất thao tác với ảnh.";
        } catch {
            return "Không thể hoàn tất thao tác với ảnh.";
        }
    }

    function showStatus(message) {
        status.textContent = message;
        status.hidden = false;
        error.hidden = true;
    }

    function showError(message) {
        error.textContent = message;
        error.hidden = false;
        status.hidden = true;
    }

    function clearMessage() {
        status.hidden = true;
        error.hidden = true;
    }

    function replaceViewerUrl(url) {
        if (viewerUrl) {
            URL.revokeObjectURL(viewerUrl);
        }
        viewerUrl = url;
        viewerImage.src = url;
    }

    function closeViewer() {
        if (viewer.open) {
            viewer.close();
        }
        if (viewerUrl) {
            URL.revokeObjectURL(viewerUrl);
            viewerUrl = undefined;
        }
        viewerImage.removeAttribute("src");
    }

    function formatBytes(bytes) {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    }

    function formatDate(value) {
        return new Intl.DateTimeFormat("vi-VN", { dateStyle: "medium" }).format(new Date(value));
    }
})();
