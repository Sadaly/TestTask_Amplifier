using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Models;

namespace Api.Controllers
{
    /// <summary>
    /// Контроллер для управления операциями прихода товаров на склад
    /// Обеспечивает CRUD-операции и фильтрацию данных
    /// </summary>
    public class ProductArrivalsController : Controller
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Конструктор контроллера с внедрением зависимости контекста базы данных
        /// </summary>
        /// <param name="context">Контекст базы данных</param>
        public ProductArrivalsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: ProductArrivals
        /// <summary>
        /// Отображает список всех операций прихода с возможностью фильтрации по периоду дат
        /// </summary>
        /// <param name="startDate">Начальная дата фильтрации (опционально)</param>
        /// <param name="endDate">Конечная дата фильтрации (опционально)</param>
        /// <returns>Представление со списком операций прихода</returns>
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            // Начальный запрос с включением связанных данных о товарах
            var query = _context.ProductArrivals.Include(p => p.Product).AsQueryable();

            // Фильтрация по начальной дате, если она указана
            if (startDate.HasValue)
            {
                query = query.Where(p => p.Date >= startDate.Value.Date);
            }

            // Фильтрация по конечной дате, если она указана
            // Добавляем 1 день для включения всех операций последнего дня периода
            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1);
                query = query.Where(p => p.Date < endOfDay);
            }

            // Передача параметров фильтрации во ViewBag для сохранения состояния фильтра в представлении
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;

            return View(await query.ToListAsync());
        }

        // GET: ProductArrivals/Details/5
        /// <summary>
        /// Отображает детальную информацию об операции прихода
        /// </summary>
        /// <param name="id">ID операции прихода</param>
        /// <returns>Представление с детальной информацией или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Details(Guid? id)
        {
            // Проверка наличия ID
            if (id == null)
            {
                return NotFound();
            }

            // Получение операции прихода вместе со связанным товаром
            var productArrival = await _context.ProductArrivals
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            // Проверка существования записи
            if (productArrival == null)
            {
                return NotFound();
            }

            return View(productArrival);
        }

        // GET: ProductArrivals/Create
        /// <summary>
        /// Отображает форму для создания новой операции прихода
        /// </summary>
        /// <returns>Представление с формой создания</returns>
        public IActionResult Create()
        {
            // Загрузка списка товаров для выпадающего списка
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title");
            return View();
        }

        // POST: ProductArrivals/Create
        /// <summary>
        /// Обрабатывает создание новой операции прихода
        /// </summary>
        /// <param name="productArrival">Модель операции прихода</param>
        /// <returns>Перенаправление на список при успешном создании или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ProductId,Amount,Date")] ProductArrival productArrival)
        {
            // Удаление ошибки валидации для навигационного свойства (не требуется для сохранения)
            ModelState.Remove(nameof(ProductArrival.Product));

            if (ModelState.IsValid)
            {
                // Генерация нового уникального идентификатора
                productArrival.Id = Guid.NewGuid();
                _context.Add(productArrival);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // При ошибке валидации перезагружаем список товаров
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productArrival.ProductId);
            return View(productArrival);
        }

        // GET: ProductArrivals/Edit/5
        /// <summary>
        /// Отображает форму редактирования существующей операции прихода
        /// </summary>
        /// <param name="id">ID операции прихода</param>
        /// <returns>Представление с формой редактирования или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var productArrival = await _context.ProductArrivals.FindAsync(id);
            if (productArrival == null)
            {
                return NotFound();
            }

            // Загрузка списка товаров с предвыделением текущего товара
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productArrival.ProductId);
            return View(productArrival);
        }

        // POST: ProductArrivals/Edit/5
        /// <summary>
        /// Обрабатывает обновление существующей операции прихода
        /// </summary>
        /// <param name="id">ID операции для обновления</param>
        /// <param name="productArrival">Модель с обновленными данными</param>
        /// <returns>Перенаправление на список при успешном обновлении или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("ProductId,Amount,Date,Id")] ProductArrival productArrival)
        {
            // Проверка соответствия ID из URL и модели
            if (id != productArrival.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(productArrival);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Проверка существования сущности при конфликте параллелизма
                    if (!ProductArrivalExists(productArrival.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            // При ошибке валидации перезагружаем список товаров
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productArrival.ProductId);
            return View(productArrival);
        }

        // GET: ProductArrivals/Delete/5
        /// <summary>
        /// Отображает страницу подтверждения удаления операции прихода
        /// </summary>
        /// <param name="id">ID операции прихода</param>
        /// <returns>Представление с подтверждением удаления или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var productArrival = await _context.ProductArrivals
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (productArrival == null)
            {
                return NotFound();
            }

            return View(productArrival);
        }

        // POST: ProductArrivals/Delete/5
        /// <summary>
        /// Обрабатывает удаление операции прихода с проверкой на сохранение положительного остатка товара
        /// </summary>
        /// <param name="id">ID удаляемой операции</param>
        /// <returns>Перенаправление на список операций</returns>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var productArrival = await _context.ProductArrivals.FindAsync(id);
            if (productArrival != null)
            {
                // Расчет текущего остатка товара после потенциального удаления операции
                var currentStock = await CalculateCurrentStock(productArrival.ProductId);
                var newStock = currentStock - productArrival.Amount;

                // Проверка на отрицательный остаток после удаления
                if (newStock < 0)
                {
                    TempData["ErrorMessage"] = "Удаление этой операции приведет к отрицательному остатку товара. Сначала удалите соответствующие расходы.";
                    return RedirectToAction(nameof(Index));
                }

                _context.ProductArrivals.Remove(productArrival);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Проверяет существование операции прихода по ID
        /// </summary>
        /// <param name="id">ID операции прихода</param>
        /// <returns>True если запись существует, иначе False</returns>
        private bool ProductArrivalExists(Guid id)
        {
            return _context.ProductArrivals.Any(e => e.Id == id);
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
    }
}