using Appota.Data;
using Appota.Models;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.Services.Account;
using Microsoft.VisualStudio.Services.Client.AccountManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Data;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace Appota.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        public List<UsersPay> users = new List<UsersPay>();
        public UsersPay user = new UsersPay();
        bool isInitialized = false;
        public ApplicationDbContext db;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, ApplicationDbContext db)
        {
            _logger = logger;
            _configuration = configuration;
            this.db = db;
            Initialize();
        }

        public ActionResult GateWayEnViet()
        {
            var items = db.Payments.Include(p => p.PaymentFees).Where(x=>x.IsActived).ToList();
            return View(items);
        }

        private async Task Initialize()
        {
            db.Database.ExecuteSqlRaw("EXEC UpdateStatusIfNoChange");
            isInitialized = true;
        }
        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> ThanhToan()
        {
            var url = "https://justcors.com/l_d26ou2gp0k/https://portal.vietcombank.com.vn/Usercontrols/TVPortal.TyGia/pXML.aspx?b=10";
            var httpClient = new HttpClient();
            var xml = await httpClient.GetStringAsync(url);
            var xdoc = XDocument.Parse(xml);

            var errorMessage = TempData["ErrorMessage"];

            var exrates = xdoc.Descendants("Exrate")
                .Select(x => new Rates
                {
                    CurrencyCode = (string)x.Attribute("CurrencyCode"),
                    CurrencyName = (string)x.Attribute("CurrencyName"),
                    Buy = (string)x.Attribute("Buy"),
                    Transfer = (string)x.Attribute("Transfer"),
                    Sell = (string)x.Attribute("Sell")
                })
                .ToList();
            return View(exrates);
        }
        private static Random random = new Random();

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HttpPost]
        public async Task<IActionResult> XacNhanThanhToan(string paymentType, long TotalPay, string userName, string? requestType)
        {
            if(userName == null)
            {
                TempData["ErrorMessage"] = "Vui lòng nhập tên người dùng";
                return RedirectToAction("ThanhToan", "Home");
            }

            if (paymentType == "Appota")
            {
                var paymentApiConfig = _configuration.GetSection("PaymentApi");
                var SecretKey = paymentApiConfig["SecretKey"];
                var bankCode = "";
                var clientIp = "103.53.171.142";
                var notifyUrl = paymentApiConfig["notifyUrl"];
                var redirectUrl = paymentApiConfig["redirectUrl"];
                var partnerCode = paymentApiConfig["PartnerCode"];
                var ApiKey = paymentApiConfig["ApiKey"];
                TempData["ApiKey"] = ApiKey;
                TempData["SecretKey"] = SecretKey;
                TempData["partnerCode"] = partnerCode;
                var token = GenerateJwtToken(partnerCode, ApiKey, SecretKey);
                TempData["AppotaToken"] = token;
                var amount = TotalPay;
                user.Amount = amount;

                TempData["Amount"] = amount.ToString();

                var orderId = RandomString(10);

                user.Name = userName;
                var orderInfo = userName;
                user.OrderId = orderId;
                user.CreatedDate = DateTime.Now;
                user.PaymentType = paymentType;
                TempData["orderId"] = orderId;
                TempData["orderInfo"] = orderInfo;
                TempData["paymentType"] = paymentType;
                var extraData = "";
                var paymentMethod = "";

                if (requestType == "APPOTABANK")
                {
                    var apiUrl = paymentApiConfig["ApiUrl"];
                    var signatureData = $"amount={amount}&bankCode={bankCode}&clientIp={clientIp}&extraData={extraData}&notifyUrl={notifyUrl}&orderId={orderId}&orderInfo={orderInfo}&paymentMethod={paymentMethod}&redirectUrl={redirectUrl}";
                    var signature = GenerateSignature(signatureData, SecretKey);
                    var data = new
                    {
                        amount = amount,
                        orderId = orderId,
                        orderInfo = orderInfo,
                        bankCode = bankCode,
                        paymentMethod = paymentMethod,
                        clientIp = clientIp,
                        extraData = extraData,
                        notifyUrl = notifyUrl,
                        redirectUrl = redirectUrl,
                        signature = signature,
                    };
                    user.PaymentStatus = "Đang chờ thanh toán";
                    db.UsersPays.Add(user);
                    db.SaveChanges();
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            client.DefaultRequestHeaders.Add("X-APPOTAPAY-AUTH", token);
                            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(apiUrl, content);

                            if (response.IsSuccessStatusCode)
                            {
                                var responseContent = await response.Content.ReadAsStringAsync();
                                //var responseData = JsonConvert.DeserializeObject<ApiQRModel>(responseContent);
                                //var errorCode = responseData.errorCode;
                                //var paymentUrl = responseData.paymentUrl;
                                JObject jmessage = JObject.Parse(responseContent);

                                var signatureResponse = jmessage.GetValue("signature").ToString();

                                TempData["AppotaSignature"] = signatureResponse;
                                return Redirect(jmessage.GetValue("paymentUrl").ToString());
                            }
                            else
                            {
                                ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                                return View();
                            }

                        }
                        catch (Exception ex)
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return View();
                        }
                    }

                }
                else
                {
                    var apiUrl = paymentApiConfig["QRApi"];
                    var signatureData = $"amount={amount}&clientIp={clientIp}&extraData={extraData}&notifyUrl={notifyUrl}&orderId={orderId}&orderInfo={orderInfo}&redirectUrl={redirectUrl}";
                    var signature = GenerateSignature(signatureData, SecretKey);
                    var data = new
                    {
                        orderId = orderId,
                        orderInfo = orderInfo,
                        amount = amount,
                        notifyUrl = notifyUrl,
                        redirectUrl = redirectUrl,
                        extraData = extraData,
                        clientIp = clientIp,
                        signature = signature,
                    };
                    user.PaymentStatus = "Đang chờ thanh toán";
                    db.UsersPays.Add(user);
                    db.SaveChanges();
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            client.DefaultRequestHeaders.Add("X-APPOTAPAY-AUTH", token);
                            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(apiUrl, content);

                            if (response.IsSuccessStatusCode)
                            {
                                var responseContent = await response.Content.ReadAsStringAsync();
                                //var responseData = JsonConvert.DeserializeObject<ApiQRModel>(responseContent);
                                //var errorCode = responseData.errorCode;
                                //var paymentUrl = responseData.paymentUrl;
                                JObject jmessage = JObject.Parse(responseContent);

                                var signatureResponse = jmessage.GetValue("signature").ToString();

                                TempData["AppotaSignature"] = signatureResponse;
                                var qrCode = jmessage.GetValue("qrData")["qrCodeUrl"].ToString();
                                var qrTime = jmessage.GetValue("qrData")["qrCodeExpiryIn"].ToString();

                                TempData["QRTime"] = qrTime;
                                TempData["QRCode"] = qrCode;

                                return RedirectToAction("AppotaQRPay", "Home");


                                //return Redirect(qrData.ToString());

                            }
                            else
                            {
                                ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                                return View();
                            }

                        }
                        catch (Exception ex)
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return View();
                        }
                    }
                }
             
            }
            else if (paymentType == "Momo")
            {
                var Momo = _configuration.GetSection("Momo");
                var apiUrl = Momo["ApiUrl"];
                var PartnerCode = Momo["PartnerCode"];
                var ApiKey = Momo["ApiKey"];
                var SecretKey = Momo["SecretKey"];
                string lang = "vi";
                var extraData = "";
                var orderInfo = "Thanh toán hoá đơn MOMO";
                var redirectUrl = Momo["redirectUrl"];
                string requestId = DateTime.Now.Ticks.ToString();

                string amount = TotalPay.ToString();

                var orderId = RandomString(10);

                string ipnUrl = "https://localhost:44370/";
                //requestType = "payWithATM";

                user.Amount = TotalPay;
                user.Name = userName;
                user.CreatedDate = DateTime.Now;
                user.PaymentType = paymentType;
                user.OrderId = orderId;

                TempData["orderId"] = orderId;
                TempData["orderInfo"] = orderInfo;
                TempData["paymentType"] = paymentType;
                TempData["partnerCode"] = PartnerCode;
                TempData["requestId"] = requestId;
                TempData["ApiKey"] = ApiKey;
                TempData["SecretKey"] = SecretKey;

                var signatureData = $"accessKey={ApiKey}&amount={amount}&extraData={extraData}&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={PartnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}";
                var signature = GenerateSignature(signatureData, SecretKey);

                var data = new
                {
                    partnerCode = PartnerCode,
                    partnerName = userName,
                    storeId = "MoMoTestStore",
                    requestType = requestType,
                    ipnUrl = ipnUrl,
                    redirectUrl = redirectUrl,
                    orderId = orderId,
                    amount = amount,
                    lang = lang,
                    orderInfo = orderInfo,
                    requestId = requestId,
                    extraData = extraData,
                    signature = signature
                };
                user.PaymentStatus = "Đang chờ thanh toán";
                db.UsersPays.Add(user);
                db.SaveChanges();

                using (var client = new HttpClient())
                {
                    try
                    {
                        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(apiUrl, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();

                            JObject jmessage = JObject.Parse(responseContent);
                            return Redirect(jmessage.GetValue("payUrl").ToString());
                        }
                        else
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return View();
                        }

                    }
                    catch (Exception ex)
                    {
                        ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                        return View();
                    }
                    return View();
                }
                return View();
            }
            else
            {
                TempData["ErrorMessage"] = "Vui lòng chọn phương thức thanh toán";
                return RedirectToAction("ThanhToan", "Home");
            }

        }

        public ActionResult AppotaQRPay()
        {
            var qrCode = TempData["QRCode"] as string;
            var qrTime = TempData["QRTime"] as string;
            var amountString = TempData["Amount"] as string;

            var amount = int.Parse(amountString);

            var orderId = TempData["orderId"] as string;
            return View();
        }

        public ActionResult GetListUser()
        {
            var items = db.UsersPays.OrderByDescending(x=>x.Id).ToList();
            return View(items);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitThanhToan(string paymentType, long TotalPay, string userName, string? requestType)
        {
            if (userName == null)
            {
                TempData["ErrorMessage"] = "Vui lòng nhập tên người dùng";
                return RedirectToAction("GateWayEnViet", "Home");
            }

            if (paymentType == "Appota")
            {
                var paymentApiConfig = _configuration.GetSection("PaymentApi");
                var SecretKey = paymentApiConfig["SecretKey"];
                var bankCode = "";
                var clientIp = "103.53.171.142";
                var notifyUrl = paymentApiConfig["notifyUrl"];
                var redirectUrl = paymentApiConfig["redirectUrl"];
                var partnerCode = paymentApiConfig["PartnerCode"];
                var ApiKey = paymentApiConfig["ApiKey"];
                TempData["ApiKey"] = ApiKey;
                TempData["SecretKey"] = SecretKey;
                TempData["partnerCode"] = partnerCode;
                var token = GenerateJwtToken(partnerCode, ApiKey, SecretKey);
                TempData["AppotaToken"] = token;
                var amount = TotalPay;
                user.Amount = amount;

                TempData["Amount"] = amount.ToString();

                var orderId = RandomString(10);

                user.Name = userName;
                var orderInfo = userName;
                user.OrderId = orderId;
                user.CreatedDate = DateTime.Now;
                user.PaymentType = paymentType;
                TempData["orderId"] = orderId;
                TempData["orderInfo"] = orderInfo;
                TempData["paymentType"] = paymentType;
                var extraData = "";
                var paymentMethod = "";

                if (requestType == "APPOTABANK" || requestType == "MOMO" || 
                    requestType == "SHOPEEPAY" || requestType == "ZALOPAY" || 
                    requestType == "APPOTA" || requestType == "VNPTWALLET" ||
                    requestType == "VIETTELPAY" ||
                    requestType == "ATM" || requestType == "CC")
                {
                    if (requestType == "MOMO")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "SHOPEEPAY")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "ZALOPAY")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "APPOTA")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "VNPTWALLET")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "VIETTELPAY")
                    {
                        bankCode = requestType;
                    }
                    else if (requestType == "ATM")
                    {
                        paymentMethod = requestType;
                    }
                    else if (requestType == "CC")
                    {
                        paymentMethod = requestType;
                    }
                    else
                    {
                        bankCode = "";
                    }

                    var apiUrl = paymentApiConfig["ApiUrl"];
                    var signatureData = $"amount={amount}&bankCode={bankCode}&clientIp={clientIp}&extraData={extraData}&notifyUrl={notifyUrl}&orderId={orderId}&orderInfo={orderInfo}&paymentMethod={paymentMethod}&redirectUrl={redirectUrl}";
                    var signature = GenerateSignature(signatureData, SecretKey);
                    var data = new
                    {
                        amount = amount,
                        orderId = orderId,
                        orderInfo = orderInfo,
                        bankCode = bankCode,
                        paymentMethod = paymentMethod,
                        clientIp = clientIp,
                        extraData = extraData,
                        notifyUrl = notifyUrl,
                        redirectUrl = redirectUrl,
                        signature = signature,
                    };
                    user.PaymentStatus = "Đang chờ thanh toán";
                    db.UsersPays.Add(user);
                    db.SaveChanges();
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            client.DefaultRequestHeaders.Add("X-APPOTAPAY-AUTH", token);
                            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(apiUrl, content);

                            if (response.IsSuccessStatusCode)
                            {
                                var responseContent = await response.Content.ReadAsStringAsync();
                                //var responseData = JsonConvert.DeserializeObject<ApiQRModel>(responseContent);
                                //var errorCode = responseData.errorCode;
                                //var paymentUrl = responseData.paymentUrl;
                                JObject jmessage = JObject.Parse(responseContent);

                                var signatureResponse = jmessage.GetValue("signature").ToString();

                                TempData["AppotaSignature"] = signatureResponse;
                                return Redirect(jmessage.GetValue("paymentUrl").ToString());
                            }
                            else
                            {
                                ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                                return RedirectToAction("GateWayEnViet","Home");
                            }

                        }
                        catch (Exception ex)
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return RedirectToAction("GateWayEnViet", "Home");
                        }
                    }
                }
            }
            else if (paymentType == "Momo")
            {
                var Momo = _configuration.GetSection("Momo");
                var apiUrl = Momo["ApiUrl"];
                var PartnerCode = Momo["PartnerCode"];
                var ApiKey = Momo["ApiKey"];
                var SecretKey = Momo["SecretKey"];
                string lang = "vi";
                var extraData = "";
                var orderInfo = "Thanh toán hoá đơn MOMO";
                var redirectUrl = Momo["redirectUrl"];
                string requestId = DateTime.Now.Ticks.ToString();

                string amount = TotalPay.ToString();

                var orderId = RandomString(10);

                string ipnUrl = "https://localhost:44370/";
                //requestType = "payWithATM";

                user.Amount = TotalPay;
                user.Name = userName;
                user.CreatedDate = DateTime.Now;
                user.PaymentType = paymentType;
                user.OrderId = orderId;

                TempData["orderId"] = orderId;
                TempData["orderInfo"] = orderInfo;
                TempData["paymentType"] = paymentType;
                TempData["partnerCode"] = PartnerCode;
                TempData["requestId"] = requestId;
                TempData["ApiKey"] = ApiKey;
                TempData["SecretKey"] = SecretKey;

                var signatureData = $"accessKey={ApiKey}&amount={amount}&extraData={extraData}&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={PartnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}";
                var signature = GenerateSignature(signatureData, SecretKey);

                var data = new
                {
                    partnerCode = PartnerCode,
                    partnerName = userName,
                    storeId = "MoMoTestStore",
                    requestType = requestType,
                    ipnUrl = ipnUrl,
                    redirectUrl = redirectUrl,
                    orderId = orderId,
                    amount = amount,
                    lang = lang,
                    orderInfo = orderInfo,
                    requestId = requestId,
                    extraData = extraData,
                    signature = signature
                };
                user.PaymentStatus = "Đang chờ thanh toán";
                db.UsersPays.Add(user);
                db.SaveChanges();

                using (var client = new HttpClient())
                {
                    try
                    {
                        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(apiUrl, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();

                            JObject jmessage = JObject.Parse(responseContent);
                            return Redirect(jmessage.GetValue("payUrl").ToString());
                        }
                        else
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return RedirectToAction("GateWayEnViet", "Home");
                        }

                    }
                    catch (Exception ex)
                    {
                        ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                        return RedirectToAction("GateWayEnViet", "Home");
                    }
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Vui lòng chọn phương thức thanh toán";
                return RedirectToAction("GateWayEnViet", "Home");
            }

            TempData["ErrorMessage"] = "Phương thức thanh toán không được hỗ trợ";
            return RedirectToAction("GateWayEnViet", "Home");
        }

        public async Task<IActionResult> KetQuaThanhToanMomo()
        {
            var paymentType = TempData["paymentType"] as string;
            if (paymentType == "Momo")
            {
                var Momo = _configuration.GetSection("Momo");
                var apiUrl = Momo["QueryApi"];
                var partnerCode = TempData["partnerCode"] as string;
                var requestId = TempData["requestId"] as string;
                var orderId = TempData["orderId"] as string;
                var orderInfo = TempData["orderInfo"] as string;
                var lang = "vi";
                var ApiKey = TempData["ApiKey"] as string;
                var SecretKey = TempData["SecretKey"] as string;


                var signatureData = $"accessKey={ApiKey}&orderId={orderId}&partnerCode={partnerCode}&requestId={requestId}";
                var signature = GenerateSignature(signatureData, SecretKey);

                var data = new
                {
                    partnerCode = partnerCode,
                    requestId = requestId,
                    orderId = orderId,
                    signature = signature,
                    lang = lang
                };

                using (var client = new HttpClient())
                {
                    try
                    {
                        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(apiUrl, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();

                            JObject jmessage = JObject.Parse(responseContent);
                            var resultCode = int.Parse(jmessage.GetValue("resultCode").ToString());
                            var orderIdResponse = jmessage.GetValue("orderId").ToString();

                            var user = db.UsersPays.Where(x => x.OrderId == orderIdResponse).FirstOrDefault();
                            string resultMessage = "";
                            if (user != null)
                            {
                                switch (resultCode)
                                {
                                    case 1000:
                                        resultMessage = "Giao dịch đã được khởi tạo, chờ người dùng xác nhận thanh toán.";
                                        break;
                                    case 1001:
                                        resultMessage = "Giao dịch thanh toán thất bại do tài khoản người dùng không đủ tiền";
                                        break;
                                    case 1002:
                                        resultMessage = "Giao dịch bị từ chối do nhà phát hành tài khoản thanh toán";
                                        break;
                                    case 1003:
                                        resultMessage = "Giao dịch bị đã bị hủy.";
                                        break;
                                    case 1004:
                                        resultMessage = "Giao dịch thất bại do số tiền thanh toán vượt quá hạn mức thanh toán của người dùng.";
                                        break;
                                    case 1005:
                                        resultMessage = "Giao dịch thất bại do url hoặc QR code đã hết hạn";
                                        break;
                                    case 1006:
                                        resultMessage = "Giao dịch thất bại do người dùng đã từ chối xác nhận thanh toán.";
                                        break;
                                    case 1007:
                                        resultMessage = "Giao dịch bị từ chối vì tài khoản không tồn tại hoặc đang ở trạng thái ngưng hoạt động";
                                        break;
                                    case 4001:
                                        resultMessage = "Giao dịch bị hạn chế do người dùng chưa hoàn tất xác thực tài khoản.";
                                        break;
                                    case 4100:
                                        resultMessage = "Giao dịch thất bại do người dùng không đăng nhập thành công.";
                                        break;
                                    case 9000:
                                        resultMessage = "Giao dịch thất bại do người dùng không đăng nhập thành công.";
                                        break;
                                    default:
                                        resultMessage = "Giao dịch đang xử lý";
                                        break;
                                }
                                db.Attach(user);
                                user.PaymentStatus = resultMessage;
                                user.ResultCode = resultCode;
                                db.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                db.SaveChanges();
                            }
                            return View(user);
                        }
                        else
                        {
                            ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return View();
                        }
                    }
                    catch (Exception ex)
                    {
                        ViewBag.thongbao = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                        return View();
                    }
                }
            }
          
            return View();
        }

        public async Task<IActionResult> KetQuaThanhToanAppota()
        {

            string url = Request.GetDisplayUrl();

            // Phân tích tham số từ URL
            var queryString = new Uri(url).Query;
            var queryParameters = HttpUtility.ParseQueryString(queryString);

            // Lấy các giá trị từ các tham số
            string partnerCode = queryParameters["partnerCode"];
            string apiKey = queryParameters["apiKey"];
            string amountString = queryParameters["amount"];
            long amount = int.Parse(amountString);
            string currency = queryParameters["currency"];
            string orderId = queryParameters["orderId"];
            string appotapayTransId = queryParameters["appotapayTransId"];
            string bankCode = queryParameters["bankCode"];
            string errorCode = queryParameters["errorCode"];
            string extraData = queryParameters["extraData"];
            string message = queryParameters["message"];
            string paymentMethod = queryParameters["paymentMethod"];
            string paymentType = queryParameters["paymentType"];
            string transactionTs = queryParameters["transactionTs"];

            string errorCodeString = queryParameters["errorCode"] as string;
            int resultCode = int.Parse(errorCodeString);
            string resultMessage = "";
            if (resultCode == 0)
            {
                resultMessage = "Thanh toán thành công";
            }
            else
            {
                resultMessage = "Thanh toán thất bại, Mã lỗi: " + resultCode;
            }

            var user = db.UsersPays.Where(x => x.OrderId == orderId).FirstOrDefault();
            if (user != null)
            {
                db.Attach(user);
                user.Amount = amount;
                user.ResultCode = resultCode;
                user.PaymentStatus = resultMessage;
                db.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                db.SaveChanges();
            }
            return View(user);
        }
        private string GenerateJwtToken(string partnerCode, string ApiKey, string SecretKey)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var header = new JwtHeader(new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256));
            var paymentApiConfig = _configuration.GetSection("PaymentApi");
            partnerCode = paymentApiConfig["PartnerCode"];
            var payload = new JwtPayload
            {
                {"typ", "JWT"},
                {"alg","HS256" },
                {"iss", partnerCode },
                {"jti",ApiKey + "-" },
                {"api_key", ApiKey }
            };

            var token = new JwtSecurityToken(header, payload);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateSignature(string data, string SecretKey)
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(SecretKey);

            using (HMACSHA256 hmacSHA256 = new HMACSHA256(secretKeyBytes))
            {
                byte[] hashBytes = hmacSHA256.ComputeHash(dataBytes);

                StringBuilder stringBuilder = new StringBuilder();

                foreach (byte b in hashBytes)
                {
                    stringBuilder.Append(b.ToString("x2"));
                }
                return stringBuilder.ToString();
            }
        }



        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
