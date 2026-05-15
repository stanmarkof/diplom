// mobile-menu.js
(function () {
    // Ждём полной загрузки DOM
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    function init() {
        // Создаём оверлей
        let overlay = document.querySelector('.sidebar-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.className = 'sidebar-overlay';
            document.body.appendChild(overlay);
        }

        // Находим кнопку меню
        let toggleBtn = document.querySelector('.mobile-menu-toggle');
        if (!toggleBtn) {
            toggleBtn = document.createElement('button');
            toggleBtn.className = 'mobile-menu-toggle';
            toggleBtn.innerHTML = '<i class="fas fa-bars"></i>';
            const navbarDiv = document.querySelector('.top-navbar .d-flex');
            if (navbarDiv && navbarDiv.firstChild) {
                navbarDiv.insertBefore(toggleBtn, navbarDiv.firstChild);
            }
        }

        const sidebar = document.querySelector('.sidebar');
        if (!sidebar) return;

        function closeMenu() {
            sidebar.classList.remove('mobile-open');
            overlay.classList.remove('active');
            document.body.classList.remove('menu-open');
        }

        function openMenu() {
            sidebar.classList.add('mobile-open');
            overlay.classList.add('active');
            document.body.classList.add('menu-open');
        }

        function toggleMenu() {
            if (sidebar.classList.contains('mobile-open')) {
                closeMenu();
            } else {
                openMenu();
            }
        }

        // Клик по кнопке
        toggleBtn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            toggleMenu();
        });

        // Клик по оверлею
        overlay.addEventListener('click', closeMenu);

        // Клик по ссылкам в меню
        document.querySelectorAll('.sidebar-link').forEach(function (link) {
            link.addEventListener('click', function () {
                if (window.innerWidth <= 390) {
                    closeMenu();
                }
            });
        });
    }
})();