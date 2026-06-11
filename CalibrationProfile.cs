using System.Drawing;

namespace AmbilightControllerForm
{
    public class CalibrationProfile
    {
        public Color Point100 { get; set; } = Color.White;
        public Color Point60 { get; set; } = Color.FromArgb(153, 153, 153);
        public Color Point30 { get; set; } = Color.FromArgb(76, 76, 76);
        public Color Point5 { get; set; } = Color.FromArgb(13, 13, 13);
        public Color PointMin { get; set; } = Color.FromArgb(0, 0, 0);
        
        public CalibrationProfile Clone()
        {
            return new CalibrationProfile
            {
                Point100 = this.Point100,
                Point60 = this.Point60,
                Point30 = this.Point30,
                Point5 = this.Point5,
                PointMin = this.PointMin
            };
        }
    }
}

