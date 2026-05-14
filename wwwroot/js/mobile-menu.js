// mobile-menu.js
document.addEventListener('DOMContentLoaded', function () {

    // Создаем оверлей
    let overlay = document.querySelector('.sidebar-overlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        document.body.appendChild(overlay);
    }

    // Находим или создаем кнопку меню
    let toggleBtn = document.querySelector('.mobile-menu-toggle');
    if (!toggleBtn) {
        toggleBtn = document.createElement('button');
        toggleBtn.className = 'mobile-menu-toggle';
        toggleBtn.innerHTML = '<i class="fas fa-bars"></i>';
        toggleBtn.setAttribute('aria-label', 'Меню');

        // Вставляем в начало .d-flex внутри .top-navbar
        const navbarFlex = document.querySelector('.top-navbar .d-flex');
        if (navbarFlex) {
            navbarFlex.insertBefore(toggleBtn, navbarFlex.firstChild);
        }
    }

    const sidebar = document.querySelector('.sidebar');
    if (!sidebar) return;

    // Функции
    function openMenu() {
        sidebar.classList.add('mobile-open');
        overlay.classList.add('active');
        document.body.style.overflow = 'hidden';
    }

    function closeMenu() {
        sidebar.classList.remove('mobile-open');
        overlay.classList.remove('active');
        document.body.style.overflow = '';
    }

    function toggleMenu() {
        if (sidebar.classList.contains('mobile-open')) {
            closeMenu();
        } else {
            openMenu();
        }
    }

    // События
    toggleBtn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        toggleMenu();
    });

    overlay.addEventListener('click', closeMenu);

    // Закрыть при изменении размера окна на десктоп
    window.addEventListener('resize', function () {
        if (window.innerWidth > 768) {
            closeMenu();
        }
    });

    // Закрыть при клике на Escape
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && sidebar.classList.contains('mobile-open')) {
            closeMenu();
        }
    });

    // Фикс отступа под шапку
    function fixHeaderOffset() {
        const navbar = document.querySelector('.top-navbar');
        const mainContent = document.querySelector('.main-content');
        if (window.innerWidth <= 768 && navbar && mainContent) {
            const height = navbar.offsetHeight;
            mainContent.style.marginTop = height + 'px';
        }
    }

    fixHeaderOffset();
    window.addEventListener('resize', fixHeaderOffset);
});