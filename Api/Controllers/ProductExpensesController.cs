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
    /// Контроллер для управления операциями расхода товаров со склада
    /// Обеспечивает CRUD-операции и проверку остатков товаров перед списанием
    /// </summary>
    public class ProductExpensesController : Controller
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Конструктор контроллера с внедрением зависимости контекста базы данных
        /// </summary>
        /// <param name="context">Контекст базы данных для работы с сущностями</param>
        public ProductExpensesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: ProductExpenses
        /// <summary>
        /// Отображает список всех операций расхода с возможностью фильтрации по периоду дат
        /// </summary>
        /// <param name="startDate">Начальная дата фильтрации (опционально)</param>
        /// <param name="endDate">Конечная дата фильтрации (опционально)</param>
        /// <returns>Представление со списком операций расхода</returns>
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            // Начальный запрос с включением связанных данных о товарах
            var query = _context.ProductExpenses.Include(p => p.Product).AsQueryable();

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

            // Передача параметров фильтрации во ViewBag для сохранения состояния в представлении
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;

            return View(await query.ToListAsync());
        }

        // GET: ProductExpenses/Details/5
        /// <summary>
        /// Отображает детальную информацию об операции расхода
        /// </summary>
        /// <param name="id">ID операции расхода</param>
        /// <returns>Представление с детальной информацией или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Details(Guid? id)
        {
            // Проверка наличия ID
            if (id == null)
            {
                return NotFound();
            }

            // Получение операции расхода вместе со связанным товаром
            var productExpense = await _context.ProductExpenses
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            // Проверка существования записи
            if (productExpense == null)
            {
                return NotFound();
            }

            return View(productExpense);
        }

        // GET: ProductExpenses/Create
        /// <summary>
        /// Отображает форму для создания новой операции расхода
        /// </summary>
        /// <returns>Представление с формой создания</returns>
        public IActionResult Create()
        {
            // Загрузка списка товаров для выпадающего списка в форме
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title");
            return View();
        }

        // POST: ProductExpenses/Create
        /// <summary>
        /// Обрабатывает создание новой операции расхода с проверкой остатков товара
        /// </summary>
        /// <param name="productExpense">Модель операции расхода</param>
        /// <returns>Перенаправление на список при успешном создании или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ProductId,Amount,Date")] ProductExpense productExpense)
        {
            // Удаление ошибки валидации для навигационного свойства Product
            // Исправлено: было nameof(ProductArrival.Product) - это ошибка в оригинале
            ModelState.Remove(nameof(ProductExpense.Product));

            if (ModelState.IsValid)
            {
                // Проверка наличия достаточного количества товара на складе
                var currentStock = await CalculateCurrentStock(productExpense.ProductId);
                if (currentStock < productExpense.Amount)
                {
                    // Добавление ошибки в ModelState при недостаточном остатке
                    ModelState.AddModelError("Amount", $"Недостаточно товара на складе. Текущий остаток: {currentStock}");
                    // Перезагрузка списка товаров для формы
                    ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productExpense.ProductId);
                    return View(productExpense);
                }

                // Генерация нового уникального идентификатора
                productExpense.Id = Guid.NewGuid();
                _context.Add(productExpense);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // При ошибке валидации перезагружаем список товаров
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productExpense.ProductId);
            return View(productExpense);
        }

        // GET: ProductExpenses/Edit/5
        /// <summary>
        /// Отображает форму редактирования существующей операции расхода
        /// </summary>
        /// <param name="id">ID операции расхода</param>
        /// <returns>Представление с формой редактирования или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var productExpense = await _context.ProductExpenses.FindAsync(id);
            if (productExpense == null)
            {
                return NotFound();
            }

            // Загрузка списка товаров с предвыделением текущего товара
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productExpense.ProductId);
            return View(productExpense);
        }

        // POST: ProductExpenses/Edit/5
        /// <summary>
        /// Обрабатывает обновление существующей операции расхода с проверкой остатков
        /// </summary>
        /// <param name="id">ID операции для обновления</param>
        /// <param name="productExpense">Модель с обновленными данными</param>
        /// <returns>Перенаправление на список при успешном обновлении или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("ProductId,Amount,Date,Id")] ProductExpense productExpense)
        {
            // Проверка соответствия ID из URL и модели
            if (id != productExpense.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Проверка наличия достаточного количества товара с учетом изменения операции
                    var originalExpense = await _context.ProductExpenses.FindAsync(id);

                    // Расчет текущего остатка с учетом корректировки:
                    // текущий остаток + старое количество (возвращаем его) - новое количество (списываем)
                    var currentStock = await CalculateCurrentStock(productExpense.ProductId);
                    var stockDiff = currentStock + originalExpense.Amount - productExpense.Amount;

                    // Проверка на отрицательный остаток после изменения операции
                    if (stockDiff < 0)
                    {
                        ModelState.AddModelError("Amount", "Недостаточно товара на складе. Текущий остаток после корректировки будет отрицательным.");
                        ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productExpense.ProductId);
                        return View(productExpense);
                    }

                    _context.Update(productExpense);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Обработка конфликта параллелизма при обновлении
                    if (!ProductExpenseExists(productExpense.Id))
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
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Title", productExpense.ProductId);
            return View(productExpense);
        }

        // GET: ProductExpenses/Delete/5
        /// <summary>
        /// Отображает страницу подтверждения удаления операции расхода
        /// </summary>
        /// <param name="id">ID операции расхода</param>
        /// <returns>Представление с подтверждением удаления или NotFound если запись не найдена</returns>
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var productExpense = await _context.ProductExpenses
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (productExpense == null)
            {
                return NotFound();
            }

            return View(productExpense);
        }

        // POST: ProductExpenses/Delete/5
        /// <summary>
        /// Обрабатывает удаление операции расхода
        /// В отличие от приходов, удаление расхода всегда безопасно для остатков
        /// </summary>
        /// <param name="id">ID удаляемой операции</param>
        /// <returns>Перенаправление на список операций</returns>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var productExpense = await _context.ProductExpenses.FindAsync(id);
            if (productExpense != null)
            {
                _context.ProductExpenses.Remove(productExpense);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Проверяет существование операции расхода по ID
        /// </summary>
        /// <param name="id">ID операции расхода</param>
        /// <returns>True если запись существует, иначе False</returns>
        private bool ProductExpenseExists(Guid id)
        {
            return _context.ProductExpenses.Any(e => e.Id == id);
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