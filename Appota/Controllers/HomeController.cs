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
using PayPal.Api;
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
using System.Security.Cryptography.Xml;
using Appota.Common;
using Microsoft.VisualStudio.Services.Organization.Client;
using ZaloPay.Helper.Crypto;
using ZaloPay.Helper;
using Appota.Models.PaymentLibrary.VNPAY;
using Appota.Models.PaymentLibrary.Paypal;

namespace Appota.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        public List<UsersPay> users = new List<UsersPay>();
        public UsersPay user = new UsersPay();
        public ApplicationDbContext db;
        public static double VNDtoUSD;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, ApplicationDbContext db)
        {
            _logger = logger;
            _configuration = configuration;
            this.db = db;
        }

        public async Task<IActionResult> GateWayEnViet()
        {
            var items = db.Payments.Include(p => p.PaymentFees).Where(x=>x.IsActived).ToList();

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

            var usdTransferRate = exrates.FirstOrDefault(rate => rate.CurrencyCode == "USD")?.Transfer;
            if (!string.IsNullOrEmpty(usdTransferRate))
            {
                VNDtoUSD = double.Parse(usdTransferRate.Replace(",", ""));
                ViewBag.VNDtoUSD = VNDtoUSD.ToString();
            }
            return View(items);
        }

        public async Task<IActionResult> Partial_NgoaiTe()
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

            var usdTransferRate = exrates.FirstOrDefault(rate => rate.CurrencyCode == "USD")?.Transfer;
            if (!string.IsNullOrEmpty(usdTransferRate))
            {
                // Loại bỏ dấu phẩy và chuyển đổi thành kiểu int
                VNDtoUSD = double.Parse(usdTransferRate.Replace(",", ""));
                ViewBag.VNDtoUSD = VNDtoUSD;
            }
            return PartialView(exrates);
        }

        public IActionResult Index()
        {
            return View();
        }
       
        private static Random random = new Random();

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }


        // cắt chuỗi
        static string GetRequestType(string input)
        {
            string[] parts = input.Split('-');
            if (parts.Length > 1) 
            {
                return parts[1]; 
            }
            return input; 
        }

        public ActionResult GetListUser()
        {
            var items = db.UsersPays.OrderByDescending(x=>x.Id).ToList();
            return View(items);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitThanhToan(string paymentType, long TotalPay, string userName, string requestType)
        {
            var PaymentActive = db.Payments.Where(x => x.Name == paymentType && x.IsActived == false).FirstOrDefault();
            if (PaymentActive != null)
            {
                TempData["ErrorMessage"] = "Phương thức thanh toán này hiện tại tạm đóng, vui lòng quay lại sau";
                return RedirectToAction("GateWayEnViet", "Home");
            }
            var requestTypeActive = db.PaymentsFee.Where(x => x.RequestType == requestType && x.IsActived == false).FirstOrDefault();
            if (requestTypeActive != null)
            {

                TempData["ErrorMessage"] = "Loại thanh toán này hiện tại tạm đóng, vui lòng quay lại sau";
                return RedirectToAction("GateWayEnViet", "Home");
            }
            if (paymentType == null || requestType == null)
            {
                TempData["ErrorMessage"] = "Vui lòng chọn phương thức/loại thanh toán";
                return RedirectToAction("GateWayEnViet", "Home");
            }
            requestType = GetRequestType(requestType); // cắt chuỗi


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
                    user.PaymentStatus = "Giao dịch đang xử lý";
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
                                TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                                return RedirectToAction("GateWayEnViet", "Home");
                            }

                        }
                        catch (Exception ex)
                        {
                            TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
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
                user.PaymentStatus = "Giao dịch đang xử lý";
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
                            TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                            return RedirectToAction("GateWayEnViet", "Home");
                        }

                    }
                    catch (Exception ex)
                    {
                        TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ với hỗ trợ.";
                        return RedirectToAction("GateWayEnViet", "Home");
                    }
                }
            }

            else if (paymentType == "Paypal")
            {
                if (requestType != null)
                {
                    var orderId = RandomString(10);
                    user.Name = userName;
                    user.PaymentType = paymentType;
                    user.Amount = TotalPay;
                    user.CreatedDate = DateTime.Now;
                    user.OrderId = orderId;
                    user.PaymentStatus = "Giao dịch đang xử lý";
                    db.UsersPays.Add(user);
                    db.SaveChanges();
                    double USD;
                    USD = (double)TotalPay / VNDtoUSD;
                    USD = Math.Round(USD, 2);

                    TempData["orderId"] = orderId;
                    TempData["TotalPay"] = USD.ToString();
                    return RedirectToAction("PaymentWithPaypal", "Home");
                }

                TempData["ErrorMessage"] = "Vui lòng chọn loại thanh toán";
                return RedirectToAction("GateWayEnViet", "Home");
            }

            else if (paymentType == "VNPAY")
            {
                var orderId = RandomString(10);
                user.Name = userName;
                user.PaymentType = paymentType;
                user.Amount = TotalPay;
                user.CreatedDate = DateTime.Now;
                user.OrderId = orderId;
                user.PaymentStatus = "Giao dịch đang xử lý";
                db.UsersPays.Add(user);
                db.SaveChanges();
                return RedirectToAction("VnPay_Payment", "Home", new { requestType = requestType, orderCode = orderId });
            }

            else if (paymentType == "ZaloPay")
            {
                

                var ZaloPayConfig = _configuration.GetSection("ZaloPay");
                string ApiUrl = ZaloPayConfig["Api_Url"];
                string app_id = ZaloPayConfig["app_id"];
                string key = ZaloPayConfig["key"];
                string redirecturl = ZaloPayConfig["redirecturl"];
                var embed_data = new { redirecturl = (string)null, bankgroup = (string)null };

                var bankCode = "";
                if (requestType == "ATM")
                {
                    embed_data = new
                    {
                        redirecturl = redirecturl,
                        bankgroup = requestType
                    };
                }
                else
                {
                    embed_data = new
                    {
                        redirecturl = redirecturl,
                        bankgroup = (string)null
                    };
                }

                if (requestType != "ATM" && requestType != "CC" && requestType != "zalopayapp")
                {
                    requestType = "";
                }

                var orderId = RandomString(10);
                

                var items = new[] { new { } };
                var param = new Dictionary<string, string>();
                var app_trans_id = orderId; // Generate a random order's ID.


                param.Add("app_id", app_id);
                param.Add("app_user", userName);
                param.Add("app_time", ZaloPay.Helper.Utils.GetTimeStamp().ToString());
                param.Add("amount", TotalPay.ToString());
                param.Add("app_trans_id", DateTime.Now.ToString("yyMMdd") + "_" + app_trans_id); // mã giao dich có định dạng yyMMdd_xxxx
                param.Add("embed_data", JsonConvert.SerializeObject(embed_data));
                param.Add("item", JsonConvert.SerializeObject(items));
                param.Add("description", "Én Việt - Thanh toán đơn hàng #" + app_trans_id);

                if(requestType == "ATM")
                {
                    param.Add("bank_code", "");
                }
                else
                {
                    param.Add("bank_code", requestType);
                }

                var data = app_id + "|" + param["app_trans_id"] + "|" + param["app_user"] + "|" + param["amount"] + "|"
                    + param["app_time"] + "|" + param["embed_data"] + "|" + param["item"];
                param.Add("mac", HmacHelper.Compute(ZaloPayHMAC.HMACSHA256, key, data));

                var result = await HttpHelper.PostFormAsync(ApiUrl, param);

                foreach (var entry in result)
                {
                   if (entry.Key != null)
                    {
                        if(entry.Key == "order_url")
                        {
                            user.Name = userName;
                            user.PaymentType = paymentType;
                            user.Amount = TotalPay;
                            user.CreatedDate = DateTime.Now;
                            user.OrderId = orderId;
                            user.PaymentStatus = "Giao dịch đang xử lý";
                            db.UsersPays.Add(user);
                            db.SaveChanges();
                            TempData["orderId"] = app_trans_id;
                            return Redirect(entry.Value.ToString());
                        }
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


        //// Thanh toán Paypal
        public ActionResult PaymentWithPaypal(string Cancel = null)
        {
            var TotalPay = TempData["TotalPay"] as string;
            var orderId = TempData["orderId"] as string;
            //getting the apiContext  
            APIContext apiContext = PaypalConfiguration.GetAPIContext();
            try
            {
                //A resource representing a Payer that funds a payment Payment Method as paypal  
                //Payer Id will be returned when payment proceeds or click to pay  
                string payerId = Request.Query["PayerID"];
                if (string.IsNullOrEmpty(payerId))
                {
                    //this section will be executed first because PayerID doesn't exist  
                    //it is returned by the create function call of the payment class  
                    // Creating a payment  
                    // baseURL is the url on which paypal sendsback the data.  
                    string baseURI = HttpContext.Request.Scheme + "://" + HttpContext.Request.Host + "/Home/KetQuaThanhToan?";
                    //here we are generating guid for storing the paymentID received in session  
                    //which will be used in the payment execution  
                    var guid = Convert.ToString((new Random()).Next(100000));
                    //CreatePayment function gives us the payment approval url  
                    //on which payer is redirected for paypal account payment  
                    var createdPayment = this.CreatePayment(apiContext, baseURI + "guid=" + guid);
                    //get links returned from paypal in response to Create function call  
                    var links = createdPayment.links.GetEnumerator();
                    string paypalRedirectUrl = null;
                    while (links.MoveNext())
                    {
                        Links lnk = links.Current;
                        if (lnk.rel.ToLower().Trim().Equals("approval_url"))
                        {
                            //saving the payapalredirect URL to which user will be redirected for payment  
                            paypalRedirectUrl = lnk.href;
                        }
                    }
                    // saving the paymentID in the key guid  
                    HttpContext.Session.SetString(guid, createdPayment.id);
                    return Redirect(paypalRedirectUrl);
                }
                else
                {
                    // This function exectues after receving all parameters for the payment  
                    var guid = Request.Query["guid"];
                    var executedPayment = ExecutePayment(apiContext, payerId, HttpContext.Session.GetString(guid));
                    //If executed payment failed then we will show payment failure message to user  
                    if (executedPayment.state.ToLower() != "approved")
                    {
                        return View("KetQuaThanhToan");
                    }
                }
            }
            catch (Exception ex)
            {
                return View("KetQuaThanhToan");
            }
            ////on successful payment, show success page to user.  

            var item = db.UsersPays.Where(x => x.OrderId == orderId).FirstOrDefault();
            if(item != null)
            {
                item.PaymentStatus = "Thanh toán thành công";
            }

            return View("KetQuaThanhToan");
        }

        /// <summary>
        /// Thanh toán VNPAY
        /// </summary>
        public ActionResult VnPay_Payment(string requestType, string orderCode)
        {
            string urlPayment = "";
            var order = db.UsersPays.FirstOrDefault(x => x.OrderId == orderCode);

            var VnPayConfig = _configuration.GetSection("VNPAY");
            //Get Config Info
            string vnp_Returnurl = VnPayConfig["vnp_Returnurl"]; //URL nhan ket qua tra ve 
            string vnp_Url = VnPayConfig["vnp_Url"]; //URL thanh toan cua VNPAY 
            string vnp_TmnCode = VnPayConfig["vnp_TmnCode"]; //Ma định danh merchant kết nối (Terminal Id)
            string vnp_HashSecret = VnPayConfig["vnp_HashSecret"]; //Secret Key

            //Build URL for VNPAY
            VnPayLibrary vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", VnPayLibrary.VERSION);
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
            var Price = order.Amount * 100;
            vnpay.AddRequestData("vnp_Amount", Price.ToString()); //Số tiền thanh toán. Số tiền không mang các ký tự phân tách thập phân, phần nghìn, ký tự tiền tệ. Để gửi số tiền thanh toán là 100,000 VND (một trăm nghìn VNĐ) thì merchant cần nhân thêm 100 lần (khử phần thập phân), sau đó gửi sang VNPAY là: 10000000
            if (requestType == "VNPAYQR")
            {
                vnpay.AddRequestData("vnp_BankCode", "VNPAYQR");
            }
            else if (requestType == "VNBANK")
            {
                vnpay.AddRequestData("vnp_BankCode", "VNBANK");
            }
            else if (requestType == "INTCARD")
            {
                vnpay.AddRequestData("vnp_BankCode", "INTCARD");
            }
            else 
            {
                vnpay.AddRequestData("vnp_BankCode", "");
            }

            vnpay.AddRequestData("vnp_CreateDate", order.CreatedDate.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", Models.PaymentLibrary.VNPAY.Utils.GetIpAddress(HttpContext));
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toán đơn hàng :" + order.OrderId);
            vnpay.AddRequestData("vnp_OrderType", "other"); //default value: other

            vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
            vnpay.AddRequestData("vnp_TxnRef", order.OrderId); // Mã tham chiếu của giao dịch tại hệ thống của merchant. Mã này là duy nhất dùng để phân biệt các đơn hàng gửi sang VNPAY. Không được trùng lặp trong ngày

            //Add Params of 2.1.0 Version
            //Billing

            urlPayment = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
            //log.InfoFormat("VNPAY URL: {0}", paymentUrl);
            return Redirect(urlPayment);
        }



        public async Task<IActionResult> KetQuaThanhToan()
        {
            string url = Request.GetDisplayUrl();

            // Phân tích tham số từ URL
            var queryString = new Uri(url).Query;
            var queryParameters = HttpUtility.ParseQueryString(queryString);

            // Lấy các giá trị từ các tham số
            string partnerCode = queryParameters["partnerCode"];
            string apiKey = queryParameters["apiKey"];
            string amountString = queryParameters["amount"];
            long amout = 0;
            if (amountString != null)
            {
                amout = int.Parse(amountString);

            }
            // APPOTA & MOMO
            string currency = queryParameters["currency"];
            string orderId = queryParameters["orderId"];
            string appotapayTransId = queryParameters["appotapayTransId"];
            string bankCode = queryParameters["bankCode"];
            string extraData = queryParameters["extraData"];
            string message = queryParameters["message"];
            string paymentMethod = queryParameters["paymentMethod"];
            string paymentType = queryParameters["paymentType"];
            string transactionTs = queryParameters["transactionTs"];
            //PAYPAL
            string guid = queryParameters["guid"];
            string paymentId = queryParameters["paymentId"];
            string token = queryParameters["token"];
            string PayerID = queryParameters["PayerID"];
            //VNPAY
            string vnp_Amount = queryParameters["vnp_Amount"];
            string vnp_BankCode = queryParameters["vnp_BankCode"];
            string vnp_BankTranNo = queryParameters["vnp_BankTranNo"];
            string vnp_CardType = queryParameters["vnp_CardType"];
            string vnp_OrderInfo = queryParameters["vnp_OrderInfo"];
            string vnp_ResponseCode = queryParameters["vnp_ResponseCode"];
            string vnp_TransactionStatus = queryParameters["vnp_TransactionStatus"];
            string vnp_TxnRef = queryParameters["vnp_TxnRef"];
            //Zalo Pay
            var ZaloPayConfig = _configuration.GetSection("ZaloPay");
            var ZaloPay_AppId = ZaloPayConfig["app_id"];
            string pmcid = queryParameters["pmcid"];
            string appid = queryParameters["appid"];
            string ZaloPayStatus = queryParameters["status"];
          

            string resultCode = "";
            string errorCode = "";
            int newResultCode = -1;
            string resultMessage = "";



            if (partnerCode == "MOMO")
            {
                 resultCode = queryParameters["resultCode"];
                 newResultCode = int.Parse(resultCode);
                if (newResultCode  == 0)
                {
                    resultMessage = "Thanh toán thành công";
                }
                else
                {
                    resultMessage = "Thanh toán thất bại";
                }
                ViewBag.KetQuaThanhToan = resultMessage;
                ViewBag.PartnerCode = partnerCode;
            }
            if (partnerCode == "APPOTAPAY")
            {
                errorCode = queryParameters["errorCode"];
                newResultCode = int.Parse(errorCode);
                if (newResultCode == 0)
                {
                    resultMessage = "Thanh toán thành công";
                }
                else
                {
                    resultMessage = "Thanh toán thất bại";
                }
                ViewBag.KetQuaThanhToan = resultMessage;
                ViewBag.PartnerCode = partnerCode;
            }
            double USDAmoutVND = 0;
            if (PayerID != null) //Paypal
            {
                orderId = TempData["orderId"] as string;
                var TotalPay = TempData["TotalPay"] as string;
                var USD = double.Parse(TotalPay);
                USDAmoutVND = USD * VNDtoUSD;
                partnerCode = "PAYPAL";
                newResultCode = 0;
                resultMessage = "Thanh toán thành công";
                ViewBag.KetQuaThanhToan = resultMessage;
                ViewBag.PartnerCode = partnerCode;
            }

            if (vnp_ResponseCode != null && vnp_TransactionStatus != null)
            {
                partnerCode = "VNPAY";
                orderId = vnp_TxnRef;

                if (vnp_ResponseCode == "00" && vnp_TransactionStatus == "00")
                {
                    resultMessage = "Thanh toán thành công";
                    newResultCode = 0;
                }
                else
                {
                    resultMessage = "Thanh toán thất bại";
                    newResultCode = -1;
                }
                ViewBag.KetQuaThanhToan = resultMessage;
                ViewBag.PartnerCode = partnerCode;
            }

            if (pmcid != null & appid != null && appid == ZaloPay_AppId)
            {
                partnerCode = "ZaloPay";
                orderId = TempData["orderId"] as string;

                if(ZaloPayStatus == "1")
                {
                    resultMessage = "Thanh toán thành công";
                    newResultCode = 0;
                }
                else
                {
                    resultMessage = "Thanh toán thất bại";
                    newResultCode = -1;
                }
                ViewBag.KetQuaThanhToan = resultMessage;
                ViewBag.PartnerCode = partnerCode;
            }

            var user = db.UsersPays.Where(x => x.OrderId == orderId).FirstOrDefault();
            if (user != null)
            {
                db.Attach(user);
                if (user.PaymentType == "Paypal")
                {
                    double roundedAmount = Math.Round(USDAmoutVND, 2);
                    user.Amount = (long)roundedAmount;
                }
                user.PaymentStatus = resultMessage;
                user.ResultCode = newResultCode;
                db.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                db.SaveChanges();
            }
            return View(user);
        }

        private PayPal.Api.Payment payment;
        private PayPal.Api.Payment ExecutePayment(APIContext apiContext, string payerId, string paymentId)
        {
            var paymentExecution = new PaymentExecution()
            {
                payer_id = payerId
            };
            this.payment = new PayPal.Api.Payment()
            {
                id = paymentId
            };
            return this.payment.Execute(apiContext, paymentExecution);
        }
        private PayPal.Api.Payment CreatePayment(APIContext apiContext, string redirectUrl)
        {
            var TotalPay = TempData["TotalPay"] as string;
            var orderId = TempData["orderId"] as string;
            var itemList = new ItemList()
            {
                items = new List<Item>()
            };

            //Adding Item Details like name, currency, price etc  
            itemList.items.Add(new Item()
            {
                name = orderId,
                currency = "USD",
                price = TotalPay,
                quantity = "1",
                sku = "1"
            });

            var payer = new Payer()
            {
                payment_method = "paypal"
            };
            // Configure Redirect Urls here with RedirectUrls object  
            var redirUrls = new RedirectUrls()
            {
                cancel_url = redirectUrl + "&Cancel=true",
                return_url = redirectUrl
            };
            // Adding Tax, shipping and Subtotal details  
            var details = new Details()
            {
                tax = "0",
                shipping = "0",
                subtotal = TotalPay
            };
            //Final amount with details  
            var amount = new Amount()
            {
                currency = "USD",
                total = (Convert.ToDecimal(details.tax) + Convert.ToDecimal(details.shipping) + Convert.ToDecimal(details.subtotal)).ToString(), // Cập nhật total
                details = details
            };
            var transactionList = new List<Transaction>();
            // Adding description about the transaction  
            var paypalOrderId = DateTime.Now.Ticks;
            transactionList.Add(new Transaction()
            {
                description = $"Invoice #{paypalOrderId}",
                invoice_number = paypalOrderId.ToString(), //Generate an Invoice No    
                amount = amount,
                item_list = itemList
            });
            this.payment = new PayPal.Api.Payment()
            {
                intent = "sale",
                payer = payer,
                transactions = transactionList,
                redirect_urls = redirUrls
            };
            // Create a payment using a APIContext  
            return this.payment.Create(apiContext);
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
