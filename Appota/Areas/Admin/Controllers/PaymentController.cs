using Appota.Data;
using Appota.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Services.Common;
using System.Web;

namespace Appota.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PaymentController : Controller
    {
        private readonly ILogger<PaymentController> _logger;
        private readonly IConfiguration _configuration;
        public ApplicationDbContext db;
        private readonly IWebHostEnvironment webHostEnvironment;


        public PaymentController(ILogger<PaymentController> logger, IConfiguration configuration, ApplicationDbContext db, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _configuration = configuration;
            this.db = db;
            this.webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index()
        {
            var items = db.Payments.Include(p => p.PaymentFees).OrderByDescending(x => x.Id).ToList();
            return View(items);
        }

        public IActionResult Add()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(Payment model, string[] FeeName, double[] Percent, string[] RequestType, decimal[] FixedCosts, IFormFile imageFile, string[] requestTypeImage)
        {
            try
            {
                model.IsActived = true;
                string defaultImage = "/lib/Content/Uploads/logo/enviet-logo.png"; 
                if (imageFile != null && imageFile.Length > 0)
                {
                    string uploadsFolder = Path.Combine(webHostEnvironment.WebRootPath, "lib", "Content", "Uploads", "logo");
                    string uniqueFileName = imageFile.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        imageFile.CopyTo(fileStream);
                    }
                    model.Image = "/lib/Content/Uploads/logo/" + uniqueFileName;
                }
                else
                {
                    model.Image = defaultImage;
                }
                db.Payments.Add(model);

                // Xử lý danh sách Phí
                if (FeeName.Length > 0 && FeeName.Any(x => x != null) && FixedCosts.Length > 0 && FixedCosts.Any(x => x >= 0) && Percent.Length > 0 && Percent.Any(x => x != 0) && RequestType.Length > 0 && RequestType.Any(x => x != null) && requestTypeImage.Length > 0)
                {

                    for (int i = 0; i < FeeName.Length; i++)
                    {
                        string feeUniqueFileName = "";

                        // Kiểm tra xem có hình ảnh được chọn không
                        if (!string.IsNullOrEmpty(requestTypeImage[i]))
                        {
                            // Sử dụng đường dẫn đã được chọn
                            feeUniqueFileName = requestTypeImage[i];
                        }
                        else
                        {
                            feeUniqueFileName = defaultImage;
                        }

                        // Tên tệp mới
                        string fileName = Path.GetFileName(feeUniqueFileName);
                        string filePath = Path.Combine("wwwroot", "lib", "Content", "Uploads", "logo", fileName);

                        // Kiểm tra xem tệp đã tồn tại hay chưa
                        if (System.IO.File.Exists(filePath))
                        {
                            // Nếu tệp đã tồn tại, ghi đè lên tệp cũ
                            // Đường dẫn của hình ảnh cần được cập nhật
                            feeUniqueFileName = "/lib/Content/Uploads/logo/" + fileName;
                        }
                        else
                        {
                            // Nếu tệp không tồn tại, lưu tên tệp mới vào thư mục
                            System.IO.File.Move(requestTypeImage[i], filePath);
                            feeUniqueFileName = "/lib/Content/Uploads/logo/" + fileName;
                        }
                        db.SaveChanges();
                        PaymentFee paymentFee = new PaymentFee
                        {
                            Name = FeeName[i],
                            Percent = Percent[i],
                            RequestType = RequestType[i],
                            FixedCosts = FixedCosts[i],
                            IsActived = true,
                            PaymentId = model.Id,
                            PaymentName = model.Name,
                            Image = feeUniqueFileName != null ? feeUniqueFileName : "/lib/Content/logo/payment-logo/enviet-logo.png"
                        };
                        db.PaymentsFee.Add(paymentFee);
                    }
                }
                else
                {
                    return Json(new { success = false, message = "Vui lòng điền đủ thông tin" });
                }
                db.SaveChanges();

                return Json(new {success = true, message = "Thêm mới phương thức thành công"});
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Vui lòng điền đủ thông tin" });
            }
        }


        [HttpPost]
        public ActionResult ChangePaymentImage(IFormFile PaymentImage, int PaymentId)
        {
            try
            {
                var payment = db.Payments.FirstOrDefault(p => p.Id == PaymentId);

                if (PaymentImage != null && PaymentImage.Length > 0)
                {
                    // Xử lý hình ảnh ở đây, ví dụ lưu vào thư mục và cập nhật đường dẫn trong cơ sở dữ liệu
                    string uploadsFolder = Path.Combine(webHostEnvironment.WebRootPath, "lib", "Content", "Uploads", "logo");
                    string uniqueFileName = PaymentImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        PaymentImage.CopyTo(fileStream);
                    }
                    payment.Image = "/lib/Content/Uploads/logo/" + uniqueFileName;
                    db.SaveChanges();
                    return Json(new { success = true });
                }
                return Json(new { success = false });

            }
            catch (Exception ex)
            {
                return Json(new { success = false });
            }
        }


        [HttpPost]
        public ActionResult Edit(string[] FeeName, double[] Percent, string[] RequestType, decimal[] FixedCosts, int PaymentID, string[] requestTypeImage)
        {
            try
            {
                var payment = db.Payments.Include(p => p.PaymentFees).FirstOrDefault(x => x.Id == PaymentID);
                // Xử lý danh sách Phí
                if (payment != null)
                {
                    if (FeeName != null && Percent != null && RequestType != null)
                    {
                        int imageCount = requestTypeImage != null ? requestTypeImage.Length : 0;

                        for (int i = 0; i < FeeName.Length; i++)
                        {
                            string feeUniqueFileName = "";

                            // Kiểm tra chỉ số hiện tại có nằm trong phạm vi của mảng requestTypeImage không
                            if (i < imageCount && !string.IsNullOrEmpty(requestTypeImage[i]))
                            {
                                // Sử dụng đường dẫn đã được chọn
                                feeUniqueFileName = requestTypeImage[i];
                            }
                            else
                            {
                                // Nếu không, gán giá trị mặc định
                                feeUniqueFileName = "/lib/Content/Uploads/logo/enviet-logo.png";
                            }


                            // Cập nhật mảng requestTypeImage
                            if (i >= imageCount)
                            {
                                Array.Resize(ref requestTypeImage, i + 1);
                                requestTypeImage[i] = "/lib/Content/Uploads/logo/enviet-logo.png";
                            }

                            // Tạo Tên tệp mới
                            string fileName = Path.GetFileName(feeUniqueFileName);
                            string filePath = Path.Combine("wwwroot", "lib", "Content", "Uploads", "logo", fileName);

                            // Kiểm tra xem tệp đã tồn tại hay chưa
                            if (System.IO.File.Exists(filePath))
                            {
                                // Nếu tệp đã tồn tại, ghi đè lên tệp cũ
                                // Đường dẫn của hình ảnh cần được cập nhật
                                feeUniqueFileName = "/lib/Content/Uploads/logo/" + fileName;
                            }
                            else
                            {
                                // Nếu tệp không tồn tại, lưu tên tệp mới vào thư mục
                                System.IO.File.Move(requestTypeImage[i], filePath);
                                feeUniqueFileName = "/lib/Content/Uploads/logo/" + fileName;
                            }

                            PaymentFee fee;
                            if (i >= payment.PaymentFees.Count)
                            {
                                fee = new PaymentFee();
                                payment.PaymentFees.Add(fee);
                            }
                            else
                            {
                                fee = payment.PaymentFees[i];
                            }

                            fee.Name = FeeName[i];
                            fee.Percent = Percent[i];
                            fee.RequestType = RequestType[i];
                            fee.FixedCosts = FixedCosts[i];
                            fee.IsActived = true;
                            fee.PaymentName = payment.Name;
                            fee.Image = feeUniqueFileName;
                        }
                        db.SaveChanges();
                    }

                }
                TempData["SuccessMessage"] = "Thêm thành công";
                return RedirectToAction("Index");
            }
            catch (Exception e)
            {
                TempData["ErrorMessage"] = "Vui lòng điền đủ thông tin";
                return RedirectToAction("Index");
            }
        }


        [HttpPost]
        public ActionResult IsActive(int id)
        {
            var item = db.Payments.Find(id);
            if (item != null)
            {
                item.IsActived = !item.IsActived;
                db.Entry(item).State = EntityState.Modified;
                db.SaveChanges();
                return Json(new { success = true, IsActived = item.IsActived });
            }
            return Json(new { success = false });
        }


        [HttpPost]
        public ActionResult FeeIsActive(int id)
        {
            var item = db.PaymentsFee.Find(id);
            if (item != null)
            {
                item.IsActived = !item.IsActived;
                db.Entry(item).State = EntityState.Modified;
                db.SaveChanges();
                return Json(new { success = true, IsActived = item.IsActived });
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public ActionResult DeleteFee(int id)
        {
            var item = db.PaymentsFee.Find(id);
            if (item != null)
            {
                db.PaymentsFee.Remove(item);
                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }
    }
}
