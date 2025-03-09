using Bogus;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS_MVC.Data;
using RestaurantPOS_MVC.Models;
using System;
using System.Collections.Generic;
using System.Linq;
namespace RestaurantPOS_MVC.Service
{
    public class SyntheticDataGenerator
    {

        private readonly ApplicationDbContext _context;

        public SyntheticDataGenerator(ApplicationDbContext context)
        {
            _context = context;
        }

        public void GenerateAndInsertData()
        {
            // Step 1: Generate Items
            var items = GenerateItems(50); // Generate 50 items
            _context.Items.AddRange(items);
            _context.SaveChanges();

            // Step 2: Generate Orders
            var orders = GenerateOrders(1000, new DateTime(2022, 1, 1), new DateTime(2023, 12, 31)); // Generate 1000 orders
            _context.Orders.AddRange(orders);
            _context.SaveChanges();

            // Step 3: Generate OrderItems
            var orderItems = GenerateOrderItems(orders, items, 10); // Generate order items
            _context.OrderItems.AddRange(orderItems);
            _context.SaveChanges();

            // Step 4: Generate Bills
            var bills = GenerateBills(orders, orderItems); // Generate bills
            _context.Bills.AddRange(bills);
            _context.SaveChanges();
        }

        public List<Item> GenerateItems(int count)
        {
            var itemFaker = new Faker<Item>()
                .RuleFor(i => i.Name, f => f.Commerce.ProductName())
                .RuleFor(i => i.Price, f => f.Random.Decimal(10, 100))
                .RuleFor(i => i.Category, f => f.Commerce.Department())
                .RuleFor(i => i.SubCategory, f => f.Commerce.Categories(1)[0]);

            return itemFaker.Generate(count);
        }



        public List<Order> GenerateOrders(int count, DateTime startDate, DateTime endDate)
        {
            var orderFaker = new Faker<Order>()
                .RuleFor(o => o.OrderDate, f => f.Date.Between(startDate, endDate))
                .RuleFor(o => o.IsPaid, f => f.Random.Bool(0.9f)); // 90% of orders are paid

            return orderFaker.Generate(count);
        }


        public List<OrderItem> GenerateOrderItems(List<Order> orders, List<Item> items, int maxItemsPerOrder)
        {
            var orderItemFaker = new Faker<OrderItem>()
                .RuleFor(oi => oi.ItemId, f => f.PickRandom(items).ItemId)
                .RuleFor(oi => oi.Quantity, f => f.Random.Int(1, 5))
                .RuleFor(oi => oi.TotalPrice, (f, oi) => items.First(i => i.ItemId == oi.ItemId).Price * oi.Quantity);

            var orderItems = new List<OrderItem>();
            foreach (var order in orders)
            {
                int itemCount = new Random().Next(1, maxItemsPerOrder + 1);
                for (int i = 0; i < itemCount; i++)
                {
                    var orderItem = orderItemFaker.Generate();
                    orderItem.OrderId = order.OrderId;
                    orderItems.Add(orderItem);
                }
            }

            return orderItems;
        }


        public List<Bill> GenerateBills(List<Order> orders, List<OrderItem> orderItems)
        {
            // Debug: Print orders and orderItems
            Console.WriteLine("Orders:");
            foreach (var order in orders)
            {
                Console.WriteLine($"OrderId: {order.OrderId}, OrderDate: {order.OrderDate}");
            }

            Console.WriteLine("OrderItems:");
            foreach (var orderItem in orderItems)
            {
                Console.WriteLine($"OrderId: {orderItem.OrderId}, ItemId: {orderItem.ItemId}, Quantity: {orderItem.Quantity}");
            }

            // Validate OrderIds in orderItems
            var invalidOrderIds = orderItems.Select(oi => oi.OrderId).Except(orders.Select(o => o.OrderId)).ToList();
            if (invalidOrderIds.Any())
            {
                throw new InvalidOperationException($"Invalid OrderIds found in OrderItems: {string.Join(", ", invalidOrderIds)}");
            }

            var bills = new List<Bill>();

            foreach (var order in orders)
            {
                // Create a Bill for each order
                var bill = new Faker<Bill>()
                    .RuleFor(b => b.OrderId, order.OrderId) // Explicitly set the OrderId first
                    .RuleFor(b => b.TotalAmount, f => orderItems
                        .Where(oi => oi.OrderId == order.OrderId)
                        .Sum(oi => oi.TotalPrice))
                    .RuleFor(b => b.PaymentDate, f => f.Date.Between(order.OrderDate, DateTime.Now))
                    .Generate();

                bills.Add(bill);
            }

            return bills;
        }



    }


}
