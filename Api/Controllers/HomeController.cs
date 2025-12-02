using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Data;
using Api.Models;
using Api.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получает данные о движении товаров (приходы и расходы) за последние 30 дней для отображения на графике
        /// </summary>
        /// <returns>Кортеж с метками дат, данными о приходах и расходах</returns>
        private async Task<(List<string> Labels, List<int> Arrivals, List<int> Expenses)> GetInventoryMovementData()
        {
            // Устанавливаем конечную дату как начало следующего дня (включаем все операции сегодняшнего дня)
            var endDate = DateTime.Today.AddDays(1);
            // Начальная дата - 30 дней назад от конечной даты
            var startDate = endDate.AddDays(-30);

            // Генерируем последовательность из 31 дня (30 дней + сегодня)
            var dates = Enumerable.Range(0, 31)
                .Select(i => startDate.AddDays(i))
                .ToList();

            // Форматируем даты для отображения на графике (например, "01 дек")
            var labels = dates.Select(d => d.ToString("dd MMM", new System.Globalization.CultureInfo("ru-RU"))).ToList();

            // Загружаем и группируем данные о приходах по датам
            var arrivalsData = await _context.ProductArrivals
                .Where(a => a.Date >= startDate && a.Date <= endDate) // Фильтруем операции за период
                .GroupBy(a => a.Date.Date) // Группируем по дате (без времени)
                .Select(g => new { Date = g.Key, Total = g.Sum(a => a.Amount) }) // Суммируем количество по каждой дате
                .ToListAsync();

            // Загружаем и группируем данные о расходах по датам
            var expensesData = await _context.ProductExpenses
                .Where(e => e.Date >= startDate && e.Date <= endDate)
                .GroupBy(e => e.Date.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            // Инициализируем массивы для хранения данных по каждому дню периода
            var arrivals = new List<int>(31);
            var expenses = new List<int>(31);

            // Заполняем массивы нулями для всех дней (на случай, если в какой-то день не было операций)
            for (int i = 0; i < 31; i++)
            {
                arrivals.Add(0);
                expenses.Add(0);
            }

            // Заполняем массив приходов данными из базы
            foreach (var item in arrivalsData)
            {
                // Вычисляем индекс дня в массиве (0 - самый старый день, 30 - сегодня)
                int dayIndex = (item.Date - startDate).Days;
                // Проверяем, что индекс находится в допустимых границах
                if (dayIndex >= 0 && dayIndex < 31)
                {
                    arrivals[dayIndex] = item.Total;
                }
            }

            // Заполняем массив расходов данными из базы
            foreach (var item in expensesData)
            {
                int dayIndex = (item.Date - startDate).Days;
                if (dayIndex >= 0 && dayIndex < 31)
                {
                    expenses[dayIndex] = item.Total;
                }
            }

            return (labels, arrivals, expenses);
        }

        /// <summary>
        /// Главная страница приложения с дашбордом статистики
        /// </summary>
        public async Task<IActionResult> Index()
        {
            // Статистика по товарам
            ViewBag.TotalProducts = await _context.Products.CountAsync();
            // Считаем товары, созданные за последний месяц (включая сегодня)
            ViewBag.NewProducts = await _context.Products
                .CountAsync(p => p.CreatedAt >= DateTime.Today.AddDays(1).AddMonths(-1));

            // Статистика по операциям за последнюю неделю
            var startDate = DateTime.Today.AddDays(-7);
            ViewBag.TotalArrivals = await _context.ProductArrivals.CountAsync();
            ViewBag.NewArrivals = await _context.ProductArrivals
                .CountAsync(a => a.Date >= startDate);

            ViewBag.TotalExpenses = await _context.ProductExpenses.CountAsync();
            ViewBag.NewExpenses = await _context.ProductExpenses
                .CountAsync(e => e.Date >= startDate);

            // Товары с низким остатком
            var productIds = await _context.Products.Select(p => p.Id).ToListAsync();

            // Загружаем все приходы по товарам и агрегируем их в словарь <ProductId, Общее количество>
            var arrivalsDict = await _context.ProductArrivals
                .Where(a => productIds.Contains(a.ProductId))
                .GroupBy(a => a.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(a => (int?)a.Amount) ?? 0 })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            // Загружаем все расходы по товарам и агрегируем их в словарь <ProductId, Общее количество>
            var expensesDict = await _context.ProductExpenses
                .Where(e => productIds.Contains(e.ProductId))
                .GroupBy(e => e.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(e => (int?)e.Amount) ?? 0 })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            // Получаем даты последних приходов для каждого товара
            var lastArrivalDates = await _context.ProductArrivals
                .Where(a => productIds.Contains(a.ProductId))
                .GroupBy(a => a.ProductId)
                .Select(g => new { ProductId = g.Key, LastDate = g.Max(a => a.Date) })
                .ToDictionaryAsync(x => x.ProductId, x => (DateTime?)x.LastDate);

            // Получаем даты последних расходов для каждого товара
            var lastExpenseDates = await _context.ProductExpenses
                .Where(e => productIds.Contains(e.ProductId))
                .GroupBy(e => e.ProductId)
                .Select(g => new { ProductId = g.Key, LastDate = g.Max(e => e.Date) })
                .ToDictionaryAsync(x => x.ProductId, x => (DateTime?)x.LastDate);

            // Вычисляем текущие остатки и формируем список товаров с низким остатком
            var lowStockItems = _context.Products.ToList().Select(p =>
            {
                // Расчет остатка: сумма всех приходов минус сумма всех расходов
                var stock = (arrivalsDict.ContainsKey(p.Id) ? arrivalsDict[p.Id] : 0) -
                           (expensesDict.ContainsKey(p.Id) ? expensesDict[p.Id] : 0);
                return new StockViewModel
                {
                    Product = p,
                    CurrentStock = stock,
                    LastArrival = lastArrivalDates.ContainsKey(p.Id) ? lastArrivalDates[p.Id] : null,
                    LastExpense = lastExpenseDates.ContainsKey(p.Id) ? lastExpenseDates[p.Id] : null
                };
            })
            // Фильтруем товары с остатком <= 5, сортируем по возрастанию остатка и берем первые 5
            .Where(vm => vm.CurrentStock <= 5)
            .OrderBy(vm => vm.CurrentStock)
            .Take(5)
            .ToList();

            ViewBag.LowStockItems = lowStockItems.Count;
            ViewBag.LowStockItemsList = lowStockItems;

            // Получаем последние 5 операций прихода с информацией о товарах
            var recentArrivals = await _context.ProductArrivals
                .Include(a => a.Product) // Загружаем связанный товар
                .OrderByDescending(a => a.Date) // Сортируем по дате (новые сначала)
                .Take(5) // Берем последние 5 операций
                .Select(a => new {
                    Id = a.Id,
                    Date = a.Date,
                    Type = "Приход",
                    ProductName = a.Product.Title,
                    Amount = a.Amount
                }).ToListAsync();

            // Получаем последние 5 операций расхода с информацией о товарах
            var recentExpenses = await _context.ProductExpenses
                .Include(e => e.Product)
                .OrderByDescending(e => e.Date)
                .Take(5)
                .Select(e => new {
                    Id = e.Id,
                    Date = e.Date,
                    Type = "Расход",
                    ProductName = e.Product.Title,
                    Amount = e.Amount
                }).ToListAsync();

            // Объединяем приходы и расходы, сортируем по дате и берем 10 самых свежих операций
            var recentTransactions = recentArrivals.Concat(recentExpenses)
                .OrderByDescending(t => t.Date)
                .Take(10)
                .ToList();

            ViewBag.RecentTransactions = recentTransactions;

            // Получаем данные для графика движения товаров
            var chartData = await GetInventoryMovementData();
            ViewBag.ChartLabels = chartData.Labels;
            ViewBag.ChartArrivals = chartData.Arrivals;
            ViewBag.ChartExpenses = chartData.Expenses;

            return View();
        }

        /// <summary>
        /// Страница политики конфиденциальности
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }
    }
}