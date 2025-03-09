using Microsoft.ML.Data;

namespace RestaurantPOS_MVC.Models
{
    public class SalesPrediction
    {
        [ColumnName("Score")]
        public float PredictedSales { get; set; }
    }
}
