using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

var group = app.MapGroup("/api/payment/vnpay");

// TODO: Tạo URL thanh toán VnPay
group.MapPost("/create-payment", (HttpContext context, double amount) =>
{
    var parameters = new SortedList<string, string?>(new VnPayParameterComparer())
    {
        { "vnp_Version", "2.1.0" },
        { "vnp_Command", "pay" },
        { "vnp_TmnCode", "8KHODCPT" },
        { "vnp_Amount", $"{amount * 100}" },
        { "vnp_BankCode", null },
        { "vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss") },
        { "vnp_CurrCode", "VND" },
        { "vnp_IpAddr", "127.0.0.1" },
        { "vnp_Locale", "vn" },
        { "vnp_OrderInfo", $"Thanh toan hoa don VN150. So tien {amount} VND" },
        { "vnp_OrderType", "other" },
        { "vnp_ReturnUrl", "http://localhost:5104/api/payment/vnpay/return" },
        { "vnp_ExpireDate", DateTime.Now.AddMinutes(5).ToString("yyyyMMddHHmmss") },
        { "vnp_TxnRef", DateTime.Now.Ticks.ToString() }
    };
    
    var queryString = BuildQueryString(parameters);
    
    var url = $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?{queryString}";
    var signature = ComputeHmacSha256("0F3LBVJB0VN4P0CI34N1JIVREDNB3FL5", queryString);
    url += $"&vnp_SecureHash={signature}";
    
    return url;
});

// TODO: Sau khi KH thanh toán, VnPay sẽ redirect về ReturnUrl để hiển thị thông tin
group.MapGet("/return", (HttpContext context) =>
{
    var parameters = context.Request.Query
        .ToDictionary(x => x.Key, x => x.Value.ToString());
    var queryStringNotSecureHash = BuildQueryString(
        parameters.Where(x => x.Key != "vnp_SecureHash").ToDictionary()!
    );
    
    var checkSum = ComputeHmacSha256("0F3LBVJB0VN4P0CI34N1JIVREDNB3FL5", queryStringNotSecureHash);
    
    return new
    {
        CheckSum = checkSum,
        IsValidSignature = checkSum.Equals(parameters["vnp_SecureHash"]),
        ResponseSuccess = parameters["vnp_ResponseCode"].Equals("00"),
        Parameters = parameters
    };
});
        
// TODO: Sau khi KH thanh toán, VnPay sẽ call api IPN đã cài đặt trên Merchant để xử lý lưu database
group.MapGet("/ipn", (HttpContext context, ILogger<VnPayLogger> logger) =>
{
    var parameters = context.Request.Query
        .ToDictionary(x => x.Key, x => x.Value.ToString());
    logger.LogInformation("{@Result}", parameters);
});

app.Run();
return;

string ComputeHmacSha256(string key, string input)
{
    using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
    var buffer = Encoding.UTF8.GetBytes(input);
    var hash = hmac.ComputeHash(buffer);
    return BitConverter.ToString(hash).Replace("-", "").ToLower();
}

string BuildQueryString(IDictionary<string, string?> dictionary)
{
    var queries = dictionary
        .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
        .Select(pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}");
    return string.Join("&", queries);
}

internal sealed class VnPayLogger;

internal sealed class VnPayParameterComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        var vnpCompare = CompareInfo.GetCompareInfo("en-US");
        return vnpCompare.Compare(x, y, CompareOptions.Ordinal);
    }
}