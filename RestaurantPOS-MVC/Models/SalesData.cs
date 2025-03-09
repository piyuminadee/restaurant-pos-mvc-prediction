namespace RestaurantPOS_MVC.Models
{
    public class SalesData
    {
        public float Year { get; set; }
        public float Month { get; set; }
        public float DayOfWeek { get; set; } // 1=Monday, 7=Sunday
        public float IsHoliday { get; set; } // 1 if holiday, 0 otherwise
        public float Sales { get; set; }
    }
}
