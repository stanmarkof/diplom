using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Models;
using diplom.Data;

namespace diplom.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Получаем последние 6 отзывов для отображения на главной
            var latestFeedbacks = await _context.Feedbacks
                .Include(f => f.User)
                
                .OrderByDescending(f => f.CreatedAt)
                .Take(6)
                .ToListAsync();

            ViewBag.LatestFeedbacks = latestFeedbacks;
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}