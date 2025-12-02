using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Models;
using System.Collections.Generic;
using Api.Models.ViewModels;

namespace Api.Controllers
{
    /// <summary>
    /// Контроллер для управления и отображения остатков товаров на складе
    /// Предоставляет обзор текущих остатков и детальную информацию по каждому товару
    /// </summary>
    public class StockController : Controller
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Конструктор контроллера с внедрением зависимости контекста базы данных
        /// </summary>
        /// <param name="context">Контекст базы данных для работы с сущностями</param>
        public StockController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Stock
        /// <summary>
        /// Отображает сводную таблицу остатков всех товаров с возможностью сортировки и фильтрации
        /// </summary>
        /// <param name="sortOrder">Параметр сортировки: по названию или по количеству остатка</param>
        /// <param name="searchString">Строка для фильтрации товаров по названию</param>
        /// <returns>Представление со списком товаров и их остатками</returns>
        public async Task<IActionResult> Index(string sortOrder, string searchString)
        {
            // Установка параметров сортировки для передачи во View
            ViewData["TitleSortParm"] = sortOrder == "Title" ? "title_desc" : "Title";
            ViewData["StockSortParm"] = sortOrder == "Stock" ? "stock_desc" : "Stock";
            ViewData["CurrentFilter"] = searchString;

            // Начальный запрос для получения всех товаров
            var products = from p in _context.Products
                           select p;

            // Фильтрация товаров по названию, если задана строка поиска
            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(p => p.Title.Contains(searchString));
            }

            // Получение списка товаров из базы данных
            var productList = await products.ToListAsync();

            // Извлечение ID всех товаров для последующей эффективной загрузки связанных данных
            var productIds = productList.Select(p => p.Id).ToList();

            // Эффективная загрузка данных о приходах: группировка по продуктам и подсчет общего количества
            var arrivalsDict = await _context.ProductArrivals
                .Where(a => productIds.Contains(a.ProductId))
                .GroupBy(a => a.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(a => (int?)a.Amount) ?? 0 })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            // Эффективная загрузка данных о расходах: группировка по продуктам и подсчет общего количества
            var expensesDict = await _context.ProductExpenses
                .Where(e => productIds.Contains(e.ProductId))
                .GroupBy(e => e.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(e => (int?)e.Amount) ?? 0 })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            // Получение дат последних приходов для каждого товара
            var lastArrivalDates = await _context.ProductArrivals
                .Where(a => productIds.Contains(a.ProductId))
                .GroupBy(a => a.ProductId)
                .Select(g => new { ProductId = g.Key, LastDate = g.Max(a => a.Date) })
                .ToDictionaryAsync(x => x.ProductId, x => (DateTime?)x.LastDate);

            // Получение дат последних расходов для каждого товара
            var lastExpenseDates = await _context.ProductExpenses
                .Where(e => productIds.Contains(e.ProductId))
                .GroupBy(e => e.ProductId)
                .Select(g => new { ProductId = g.Key, LastDate = g.Max(e => e.Date) })
                .ToDictionaryAsync(x => x.ProductId, x => (DateTime?)x.LastDate);

            // Создание ViewModel для каждого товара с расчетом текущего остатка
            var viewModel = productList.Select(p =>
            {
                // Расчет остатка: общее количество приходов минус общее количество расходов
                var stock = (arrivalsDict.ContainsKey(p.Id) ? arrivalsDict[p.Id] : 0) -
                           (expensesDict.ContainsKey(p.Id) ? expensesDict[p.Id] : 0);

                return new StockViewModel
                {
                    Product = p,
                    CurrentStock = stock,
                    // Установка последних дат прихода/расхода, если они существуют
                    LastArrival = lastArrivalDates.ContainsKey(p.Id) ? lastArrivalDates[p.Id] : null,
                    LastExpense = lastExpenseDates.ContainsKey(p.Id) ? lastExpenseDates[p.Id] : null
                };
            }).ToList();

            // Применение сортировки на клиентской стороне
            switch (sortOrder)
            {
                case "Title":
                    viewModel = viewModel.OrderBy(vm => vm.Product.Title).ToList();
                    break;
                case "title_desc":
                    viewModel = viewModel.OrderByDescending(vm => vm.Product.Title).ToList();
                    break;
                case "Stock":
                    viewModel = viewModel.OrderBy(vm => vm.CurrentStock).ToList();
                    break;
                case "stock_desc":
                    viewModel = viewModel.OrderByDescending(vm => vm.CurrentStock).ToList();
                    break;
                default:
                    viewModel = viewModel.OrderBy(vm => vm.Product.Title).ToList();
                    break;
            }

            return View(viewModel);
        }

        // GET: Stock/Details/5
        /// <summary>
        /// Отображает детальную информацию по конкретному товару включая все операции прихода и расхода
        /// </summary>
        /// <param name="id">ID товара</param>
        /// <param name="startDate">Начальная дата фильтрации операций (опционально)</param>
        /// <param name="endDate">Конечная дата фильтрации операций (опционально)</param>
        /// <returns>Представление с детальной информацией по товару или NotFound если товар не найден</returns>
        public async Task<IActionResult> Details(Guid? id, DateTime? startDate, DateTime? endDate)
        {
            // Проверка наличия ID товара
            if (id == null)
            {
                return NotFound();
            }

            // Получение товара из базы данных
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            // Передача информации о товаре во View
            ViewBag.ProductTitle = product.Title;
            ViewBag.ProductId = id;

            // Начальные запросы для получения операций прихода и расхода по товару
            var arrivalsQuery = _context.ProductArrivals.Where(a => a.ProductId == id);
            var expensesQuery = _context.ProductExpenses.Where(e => e.ProductId == id);

            // Фильтрация операций прихода/расхода по начальной дате
            if (startDate.HasValue)
            {
                arrivalsQuery = arrivalsQuery.Where(a => a.Date >= startDate.Value.Date);
                expensesQuery = expensesQuery.Where(e => e.Date >= startDate.Value.Date);
            }

            // Фильтрация операций прихода/расхода по конечной дате
            // Добавляем 1 день для включения всех операций последнего дня периода
            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1);
                arrivalsQuery = arrivalsQuery.Where(a => a.Date < endOfDay);
                expensesQuery = expensesQuery.Where(e => e.Date < endOfDay);
            }

            // Сохранение параметров фильтрации для отображения в представлении
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;

            // Получение отфильтрованных списков операций прихода и расхода
            ViewBag.Arrivals = await arrivalsQuery.OrderByDescending(a => a.Date).ToListAsync();
            ViewBag.Expenses = await expensesQuery.OrderByDescending(e => e.Date).ToListAsync();

            // Расчет и передача текущего остатка товара
            ViewBag.CurrentStock = await CalculateCurrentStock(id.Value);

            return View();
        }

        /// <summary>
        /// Расчет текущего остатка товара на складе
        /// </summary>
        /// <param name="productId">ID товара</param>
        /// <returns>Текущий остаток товара</returns>
        private async Task<int> CalculateCurrentStock(Guid productId)
        {
            // Сумма всех приходов по товару
            var arrivals = await _context.ProductArrivals
                .Where(a => a.ProductId == productId)
                .SumAsync(a => (int?)a.Amount) ?? 0;

            // Сумма всех расходов по товару
            var expenses = await _context.ProductExpenses
                .Where(e => e.ProductId == productId)
                .SumAsync(e => (int?)e.Amount) ?? 0;

            // Остаток = приходы - расходы
            return arrivals - expenses;
        }

        /// <summary>
        /// Получение даты последней операции прихода для товара
        /// </summary>
        /// <param name="productId">ID товара</param>
        /// <returns>Дата последнего прихода или null, если приходов не было</returns>
        private async Task<DateTime?> GetLastArrivalDate(Guid productId)
        {
            return await _context.ProductArrivals
                .Where(a => a.ProductId == productId)
                .OrderByDescending(a => a.Date)
                .Select(a => (DateTime?)a.Date)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Получение даты последней операции расхода для товара
        /// </summary>
        /// <param name="productId">ID товара</param>
        /// <returns>Дата последнего расхода или null, если расходов не было</returns>
        private async Task<DateTime?> GetLastExpenseDate(Guid productId)
        {
            return await _context.ProductExpenses
                .Where(e => e.ProductId == productId)
                .OrderByDescending(e => e.Date)
                .Select(e => (DateTime?)e.Date)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Расчет остатка товара без использования async (синхронная версия)
        /// Используется для сортировки в памяти, когда асинхронный вызов невозможен
        /// </summary>
        /// <param name="productId">ID товара</param>
        /// <returns>Текущий остаток товара</returns>
        private int CalculateStockForProduct(Guid productId)
        {
            // Сумма всех приходов по товару
            var arrivals = _context.ProductArrivals
                .Where(a => a.ProductId == productId)
                .Sum(a => (int?)a.Amount) ?? 0;

            // Сумма всех расходов по товару
            var expenses = _context.ProductExpenses
                .Where(e => e.ProductId == productId)
                .Sum(e => (int?)e.Amount) ?? 0;

            // Остаток = приходы - расходы
            return arrivals - expenses;
        }
    }
}