using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Models;

namespace Api.Controllers
{
    /// <summary>
    /// Контроллер для управления каталогом товаров
    /// Обеспечивает CRUD-операции, проверку уникальности наименований
    /// и отображение информации о движении товаров
    /// </summary>
    public class ProductsController : Controller
    {
        private readonly AppDbContext _context;

        /// <summary>
        /// Конструктор контроллера с внедрением зависимости контекста базы данных
        /// </summary>
        /// <param name="context">Контекст базы данных для работы с сущностями</param>
        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Products
        /// <summary>
        /// Отображает список всех товаров в каталоге
        /// </summary>
        /// <returns>Представление со списком товаров</returns>
        public async Task<IActionResult> Index()
        {
            return View(await _context.Products.ToListAsync());
        }

        // GET: Products/Details/5
        /// <summary>
        /// Отображает детальную информацию о товаре включая остатки и движение за последний месяц
        /// </summary>
        /// <param name="id">ID товара</param>
        /// <returns>Представление с детальной информацией или NotFound если товар не найден</returns>
        public async Task<IActionResult> Details(Guid? id)
        {
            // Проверка наличия id в запросе
            if (id == null)
            {
                return NotFound();
            }

            // Получение товара из базы данных
            var product = await _context.Products
                .FirstOrDefaultAsync(m => m.Id == id);

            // Проверка существования товара
            if (product == null)
            {
                return NotFound();
            }

            // Расчет и передача текущего остатка товара во ViewBag
            ViewBag.CurrentStock = await CalculateCurrentStock(product.Id);

            // Получение информации о движении товара за последний месяц
            var startDate = DateTime.Today.AddMonths(-1);

            // Последние 5 операций прихода за последний месяц
            var arrivals = await _context.ProductArrivals
                .Where(a => a.ProductId == product.Id && a.Date >= startDate)
                .OrderByDescending(a => a.Date)
                .Take(5)
                .ToListAsync();

            // Последние 5 операций расхода за последний месяц
            var expenses = await _context.ProductExpenses
                .Where(e => e.ProductId == product.Id && e.Date >= startDate)
                .OrderByDescending(e => e.Date)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentArrivals = arrivals;
            ViewBag.RecentExpenses = expenses;

            return View(product);
        }

        // GET: Products/Create
        /// <summary>
        /// Отображает форму для создания нового товара
        /// </summary>
        /// <returns>Представление с формой создания товара</returns>
        public IActionResult Create()
        {
            return View();
        }

        // POST: Products/Create
        /// <summary>
        /// Обрабатывает создание нового товара с проверкой на уникальность наименования
        /// </summary>
        /// <param name="product">Модель товара для создания</param>
        /// <returns>Перенаправление на список товаров при успешном создании или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Title")] Product product)
        {
            // Проверка на дубликаты названий (регистронезависимая)
            if (await _context.Products.AnyAsync(p => p.Title.ToLower() == product.Title.ToLower().Trim()))
            {
                ModelState.AddModelError("Title", "Товар с таким наименованием уже существует");
            }

            // Проверка валидности модели и сохранение в БД
            if (ModelState.IsValid)
            {
                product.Id = Guid.NewGuid();
                _context.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }

        // GET: Products/Edit/5
        /// <summary>
        /// Отображает форму редактирования существующего товара
        /// </summary>
        /// <param name="id">ID товара для редактирования</param>
        /// <returns>Представление с формой редактирования или NotFound если товар не найден</returns>
        public async Task<IActionResult> Edit(Guid? id)
        {
            // Проверка наличия id в запросе
            if (id == null)
            {
                return NotFound();
            }

            // Получение товара из базы данных
            var product = await _context.Products.FindAsync(id);

            // Проверка существования товара
            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }

        // POST: Products/Edit/5
        /// <summary>
        /// Обрабатывает обновление информации о товаре с проверкой на уникальность наименования
        /// </summary>
        /// <param name="id">ID товара для обновления</param>
        /// <param name="product">Модель с обновленными данными</param>
        /// <returns>Перенаправление на список товаров при успешном обновлении или форму с ошибками</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Title,Id")] Product product)
        {
            // Проверка соответствия id из URL и модели
            if (id != product.Id)
            {
                return NotFound();
            }

            // Проверка на дубликаты названий, исключая текущий товар
            if (await _context.Products.AnyAsync(p =>
                p.Id != id && p.Title.ToLower() == product.Title.ToLower().Trim()))
            {
                ModelState.AddModelError("Title", "Товар с таким наименованием уже существует");
            }

            // Проверка валидности модели и обновление в БД
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(product);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Проверка существования товара при конфликте параллелизма
                    if (!ProductExists(product.Id))
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
            return View(product);
        }

        // GET: Products/Delete/5
        /// <summary>
        /// Отображает страницу подтверждения удаления товара
        /// с проверкой наличия связанных операций прихода/расхода
        /// </summary>
        /// <param name="id">ID товара для удаления</param>
        /// <returns>Представление с подтверждением удаления или NotFound если товар не найден</returns>
        public async Task<IActionResult> Delete(Guid? id)
        {
            // Проверка наличия id в запросе
            if (id == null)
            {
                return NotFound();
            }

            // Получение товара из базы данных
            var product = await _context.Products
                .FirstOrDefaultAsync(m => m.Id == id);

            // Проверка существования товара
            if (product == null)
            {
                return NotFound();
            }

            // Проверка наличия связанных операций прихода/расхода
            ViewBag.HasTransactions = await _context.ProductArrivals.AnyAsync(a => a.ProductId == id) ||
                                      await _context.ProductExpenses.AnyAsync(e => e.ProductId == id);

            return View(product);
        }

        // POST: Products/Delete/5
        /// <summary>
        /// Обрабатывает удаление товара с проверкой наличия связанных операций
        /// </summary>
        /// <param name="id">ID товара для удаления</param>
        /// <returns>Перенаправление на список товаров с сообщением о результате операции</returns>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            // Загрузка товара с включенными связанными коллекциями приходов и расходов
            var product = await _context.Products
                .Include(p => p.Arrivals)
                .Include(p => p.Expenses)
                .FirstOrDefaultAsync(p => p.Id == id);

            // Проверка существования товара
            if (product == null)
            {
                return NotFound();
            }

            // Проверка наличия связанных операций
            bool hasTransactions = (product.Arrivals?.Any() ?? false) ||
                                   (product.Expenses?.Any() ?? false);

            // Если есть связанные операции - запрещаем удаление
            if (hasTransactions)
            {
                TempData["ErrorMessage"] = "Невозможно удалить товар «" + product.Title +
                    "», так как по нему есть операции прихода или расхода.";
                return RedirectToAction(nameof(Index));
            }

            // Удаление товара из базы данных
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            // Успешное удаление
            TempData["SuccessMessage"] = "Товар «" + product.Title + "» успешно удалён.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Проверяет существование товара по ID
        /// </summary>
        /// <param name="id">ID товара</param>
        /// <returns>True если товар существует, иначе False</returns>
        private bool ProductExists(Guid id)
        {
            return _context.Products.Any(e => e.Id == id);
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