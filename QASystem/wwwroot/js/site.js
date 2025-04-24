/*$(() => {
    // Khởi động kết nối SignalR
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/notificationHub")
        .build();

    connection.start()
        .then(() => console.log("Connected to notification hub"))
        .catch(err => console.error(err));

    // Lắng nghe sự kiện ReceiveNotification
    connection.on("ReceiveNotification", function (message) {
        const notificationList = document.querySelector(".dropdown-menu");
        notificationList.insertAdjacentHTML("afterbegin", `<li><a class="dropdown-item fw-bold">${message}</a></li>`);

        const badge = document.querySelector(".badge.bg-danger");
        badge.textContent = parseInt(badge.textContent) + 1;
    });
});*/
