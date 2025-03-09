using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS_MVC.Data;
using RestaurantPOS_MVC.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.CodeAnalysis.Elfie.Serialization;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.ML;
using Microsoft.ML.Data;

[Authorize(Roles = "Manager")]
public class ManagerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly MLContext _mlContext;
    private ITransformer _trainedModel;

    public ManagerController(ApplicationDbContext context)
    {
        _context = context;
        _mlContext = new MLContext();
        _trainedModel = TrainModel();
    }

    // View all menu items
    public async Task<IActionResult> MenuManagement()
    {
        var items = await _context.Items.ToListAsync();
        return View(items);
    }

    // Add a new menu item
    public IActionResult AddMenuItem()
    {
        ViewBag.Categories = _context.Items.Select(i => i.Category).Distinct().ToList();
        ViewBag.SubCategories = _context.Items.Select(i => i.SubCategory).Distinct().ToList();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> AddMenuItem(Item item, string? NewCategory, string? NewSubCategory)
    {
        // Use new category or existing one
        if (!string.IsNullOrEmpty(NewCategory))
        {
            item.Category = NewCategory;
        }

        // Use new subcategory or existing one
        if (!string.IsNullOrEmpty(NewSubCategory))
        {
            item.SubCategory = NewSubCategory;
        }

        if (ModelState.IsValid)
        {
            _context.Items.Add(item);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(MenuManagement));
        }

        ViewBag.Categories = _context.Items.Select(i => i.Category).Distinct().ToList();
        ViewBag.SubCategories = _context.Items.Select(i => i.SubCategory).Distinct().ToList();
        return View(item);
    }

    // Edit an existing menu item
    public async Task<IActionResult> EditMenuItem(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }
        return View(item);
    }



    [HttpPost]
    public async Task<IActionResult> EditMenuItem(Item item)
    {
        if (ModelState.IsValid)
        {
            _context.Items.Update(item);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(MenuManagement));
        }
        return View(item);
    }

    // Delete a menu item
    public async Task<IActionResult> DeleteMenuItem(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item != null)
        {
            _context.Items.Remove(item);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(MenuManagement));
    }

    // View orders with support for day and year filtering
    public async Task<IActionResult> OrdersReport(string reportType, DateTime? reportDate, int? reportYear)
    {
        var ordersQuery = _context.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Item)  // Ensure the Item is included as well
            .AsQueryable();

        // Handle filtering based on report type
        if (reportType == "day" && reportDate.HasValue)
        {
            // Filter by selected date
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Date == reportDate.Value.Date);
            ViewData["ReportDate"] = reportDate.Value.ToString("yyyy-MM-dd");
            ViewData["ReportType"] = "day";
        }
        else if (reportType == "year" && reportYear.HasValue)
        {
            // Filter by selected year
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Year == reportYear.Value);
            ViewData["ReportYear"] = reportYear.Value;
            ViewData["ReportType"] = "year";
        }
        else
        {
            // Default to today's date if no date or year is selected
            reportDate = DateTime.Today;
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Date == reportDate.Value.Date);
            ViewData["ReportDate"] = reportDate.Value.ToString("yyyy-MM-dd");
            ViewData["ReportType"] = "day";
        }

        var orders = await ordersQuery.ToListAsync();
        return View(orders);
    }


    // Export orders to CSV with support for day and year filtering
    [HttpGet]
    public async Task<IActionResult> ExportOrdersToCsv(string reportType, DateTime? reportDate, int? reportYear)
    {
        IQueryable<Order> ordersQuery = _context.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Item);

        // Handle filtering based on report type
        if (reportType == "day" && reportDate.HasValue)
        {
            // Filter by selected date
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Date == reportDate.Value.Date);
        }
        else if (reportType == "year" && reportYear.HasValue)
        {
            // Filter by selected year
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Year == reportYear.Value);
        }
        else
        {
            // Default to today's date if no date or year is selected
            reportDate = DateTime.Today;
            ordersQuery = ordersQuery.Where(o => o.OrderDate.Date == reportDate.Value.Date);
        }

        var orders = await ordersQuery
            .OrderBy(o => o.OrderDate)
            .Select(o => new
            {
                o.OrderId,
                o.OrderDate,
                Items = string.Join(", ", o.OrderItems.Select(oi => $"{oi.Item.Name} (x{oi.Quantity})")),
                Total = o.OrderItems.Sum(oi => oi.TotalPrice),
                Status = o.IsPaid ? "Paid" : "Pending"
            })
            .ToListAsync();

        // Generate CSV
        using (var memoryStream = new MemoryStream())
        using (var writer = new StreamWriter(memoryStream))
        using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(orders);
            writer.Flush();
            var result = memoryStream.ToArray();

            // Set the filename based on the report type
            string fileName;
            if (reportType == "day" && reportDate.HasValue)
            {
                fileName = $"orders_{reportDate.Value:yyyyMMdd}.csv";
            }
            else if (reportType == "year" && reportYear.HasValue)
            {
                fileName = $"orders_{reportYear.Value}.csv";
            }
            else
            {
                fileName = "orders.csv";
            }

            return File(result, "text/csv", fileName);
        }
    }

    // Export sales data for the past 30 days
    [HttpGet]
    public async Task<IActionResult> ExportSales()
    {
        var past30Days = DateTime.Now.AddDays(-30);
        var salesData = await _context.Bills
            .Include(b => b.Order)
            .Where(b => b.PaymentDate >= past30Days)
            .OrderBy(b => b.PaymentDate)
            .Select(b => new
            {
                b.BillId,
                b.OrderId,
                b.TotalAmount,
                b.PaymentDate,
                OrderDate = b.Order.OrderDate,
                IsPaid = b.Order.IsPaid
            })
            .ToListAsync();

        using (var memoryStream = new MemoryStream())
        using (var writer = new StreamWriter(memoryStream))
        using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(salesData);
            writer.Flush();
            var result = memoryStream.ToArray();
            return File(result, "text/csv", "sales_last_30_days.csv");
        }
    }

    // View sales with support for day and year filtering
    public async Task<IActionResult> SalesReport(string reportType, DateTime? reportDate, int? reportYear)
    {
        var salesQuery = _context.Bills
            .Include(b => b.Order)
            .AsQueryable();

        // Handle filtering based on report type
        if (reportType == "day" && reportDate.HasValue)
        {
            // Filter by selected date
            salesQuery = salesQuery.Where(b => b.PaymentDate.Date == reportDate.Value.Date);
            ViewData["ReportDate"] = reportDate.Value.ToString("yyyy-MM-dd");
            ViewData["ReportType"] = "day";
        }
        else if (reportType == "year" && reportYear.HasValue)
        {
            // Filter by selected year
            salesQuery = salesQuery.Where(b => b.PaymentDate.Year == reportYear.Value);
            ViewData["ReportYear"] = reportYear.Value;
            ViewData["ReportType"] = "year";
        }
        else
        {
            // Default to today's date if no date or year is selected
            reportDate = DateTime.Today;
            salesQuery = salesQuery.Where(b => b.PaymentDate.Date == reportDate.Value.Date);
            ViewData["ReportDate"] = reportDate.Value.ToString("yyyy-MM-dd");
            ViewData["ReportType"] = "day";
        }

        var sales = await salesQuery.ToListAsync();
        ViewData["TotalSales"] = sales.Sum(b => b.TotalAmount).ToString("N2");
        return View(sales);
    }


    // Export sales to CSV with support for day and year filtering
    [HttpGet]
    public async Task<IActionResult> ExportSalesToCsv(string reportType, DateTime? reportDate, int? reportYear)
    {
        IQueryable<Bill> salesQuery = _context.Bills
            .Include(b => b.Order);

        // Handle filtering based on report type
        if (reportType == "day" && reportDate.HasValue)
        {
            // Filter by selected date
            salesQuery = salesQuery.Where(b => b.PaymentDate.Date == reportDate.Value.Date);
        }
        else if (reportType == "year" && reportYear.HasValue)
        {
            // Filter by selected year
            salesQuery = salesQuery.Where(b => b.PaymentDate.Year == reportYear.Value);
        }
        else
        {
            // Default to today's date if no date or year is selected
            reportDate = DateTime.Today;
            salesQuery = salesQuery.Where(b => b.PaymentDate.Date == reportDate.Value.Date);
        }

        var sales = await salesQuery
            .OrderBy(b => b.PaymentDate)
            .Select(b => new
            {
                b.BillId,
                b.OrderId,
                b.TotalAmount,
                b.PaymentDate
            })
            .ToListAsync();

        // Generate CSV
        using (var memoryStream = new MemoryStream())
        using (var writer = new StreamWriter(memoryStream))
        using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(sales);
            writer.Flush();
            var result = memoryStream.ToArray();

            // Set the filename based on the report type
            string fileName;
            if (reportType == "day" && reportDate.HasValue)
            {
                fileName = $"sales_{reportDate.Value:yyyyMMdd}.csv";
            }
            else if (reportType == "year" && reportYear.HasValue)
            {
                fileName = $"sales_{reportYear.Value}.csv";
            }
            else
            {
                fileName = "sales.csv";
            }

            return File(result, "text/csv", fileName);
        }
    }



    // Export all items to CSV
    [HttpGet]
    public async Task<IActionResult> ExportItemsToCsv()
    {
        var items = await _context.Items
            .Select(i => new
            {
                i.ItemId,
                i.Name,
                i.Price,
                i.Category,
                i.SubCategory
            })
            .ToListAsync();

        using (var memoryStream = new MemoryStream())
        using (var writer = new StreamWriter(memoryStream))
        using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(items);
            writer.Flush();
            var result = memoryStream.ToArray();
            return File(result, "text/csv", "items.csv");
        }
    }

    // Predictions Action
    public async Task<IActionResult> Predictions()
    {
        var viewModel = new PredictionsViewModel();

        try
        {
            // Step 1: Get historical sales data
            viewModel.HistoricalSales = await _context.Bills
                .Include(b => b.Order)
                .Where(b => b.PaymentDate.Year == DateTime.Now.Year - 1) // Last year's data
                .GroupBy(b => b.PaymentDate.Month)
                .Select(g => new HistoricalSale
                {
                    Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key),
                    Sales = g.Sum(b => b.TotalAmount)
                })
                .ToListAsync();

            // Step 2: Predict next year's sales using ML.NET
            viewModel.PredictedSales = new List<PredictedSale>();
            foreach (var month in Enumerable.Range(1, 12))
            {
                var prediction = PredictSales(DateTime.Now.Year + 1, month);
                viewModel.PredictedSales.Add(new PredictedSale
                {
                    Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month),
                    Sales = prediction
                });
            }

            // Step 3: Get best-selling items for each month
            viewModel.BestSellingItems = await _context.OrderItems
                .Include(oi => oi.Item)
                .Include(oi => oi.Order)
                .Where(oi => oi.Order.OrderDate.Year == DateTime.Now.Year - 1) // Last year's data
                .GroupBy(oi => new { oi.Order.OrderDate.Month, oi.Item.Name })
                .Select(g => new
                {
                    Month = g.Key.Month,
                    ItemName = g.Key.Name,
                    Quantity = g.Sum(oi => oi.Quantity)
                })
                .GroupBy(g => g.Month) // Group by month
                .Select(g => new BestSellingItem
                {
                    Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key),
                    ItemName = g.OrderByDescending(x => x.Quantity).First().ItemName,
                    Quantity = g.OrderByDescending(x => x.Quantity).First().Quantity
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            // Log the error (e.g., using ILogger)
            Console.WriteLine($"Error: {ex.Message}");
        }

        return View(viewModel);
    }

    private ITransformer TrainModel()
    {
        var data = _context.Bills
            .Include(b => b.Order)
            .Select(b => new SalesData
            {
                Year = b.PaymentDate.Year,
                Month = b.PaymentDate.Month,
                DayOfWeek = (float)b.PaymentDate.DayOfWeek,
                IsHoliday = 0, // Add logic to determine holidays
                Sales = (float)b.TotalAmount
            })
            .ToList();

        var mlData = _mlContext.Data.LoadFromEnumerable(data);
        var splitData = _mlContext.Data.TrainTestSplit(mlData, testFraction: 0.2);

        var pipeline = _mlContext.Transforms.Concatenate("Features", nameof(SalesData.Year), nameof(SalesData.Month), nameof(SalesData.DayOfWeek), nameof(SalesData.IsHoliday))
     .Append(_mlContext.Regression.Trainers.FastTree(labelColumnName: nameof(SalesData.Sales), 
            numberOfLeaves: 20,
            numberOfTrees: 100,
            learningRate: 0.1));
        
         var model = pipeline.Fit(splitData.TrainSet);

        // Evaluate the model
        var predictions = model.Transform(splitData.TestSet);
        var metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: nameof(SalesData.Sales));

        Console.WriteLine($"MAE: {metrics.MeanAbsoluteError}");
        Console.WriteLine($"RMSE: {metrics.RootMeanSquaredError}");

        return model;
    }

    // Predict Sales using ML.NET
    private decimal PredictSales(int year, int month)
    {
        var predictionEngine = _mlContext.Model.CreatePredictionEngine<SalesData, SalesPrediction>(_trainedModel);
        var input = new SalesData { Year = year, Month = month };
        var prediction = predictionEngine.Predict(input);
        return (decimal)prediction.PredictedSales;
    }


    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> ItemsReport()
    {
        var items = await _context.Items.ToListAsync();  // Get all items
        return View(items);  // Pass the list of items to the view
    }




}