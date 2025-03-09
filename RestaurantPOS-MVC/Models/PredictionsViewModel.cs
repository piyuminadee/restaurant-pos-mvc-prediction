namespace RestaurantPOS_MVC.Models
{
    public class PredictionsViewModel
    {
        public List<PredictedSale> PredictedSales { get; set; }
        public List<HistoricalSale> HistoricalSales { get; set; }
        public List<BestSellingItem> BestSellingItems { get; set; }
    }
    public class PredictedSale
    {
        public string Month { get; set; }
        public decimal Sales { get; set; }
    }

    public class HistoricalSale
    {
        public string Month { get; set; }
        public decimal Sales { get; set; }
    }

    public class BestSellingItem
    {
        public string Month { get; set; }
        public string ItemName { get; set; }
        public int Quantity { get; set; }
    }
}
