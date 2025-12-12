using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Breez.Sdk.Spark;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.BreezSpark;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/{storeId}/BreezSpark")]
public class BreezSparkController : Controller
{
    private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly BreezSparkService _breezService;
    private readonly BTCPayWalletProvider _btcWalletProvider;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<BreezSparkController> _logger;

    public BreezSparkController(
        PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
        BTCPayNetworkProvider btcPayNetworkProvider,
        BreezSparkService breezService,
        BTCPayWalletProvider btcWalletProvider,
        StoreRepository storeRepository,
        ILogger<BreezSparkController> logger)
    {
        _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _breezService = breezService;
        _btcWalletProvider = btcWalletProvider;
        _storeRepository = storeRepository;
        _logger = logger;
    }


    [HttpGet("")]
    public async Task<IActionResult> Index(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        return RedirectToAction(client is null ? nameof(Configure) : nameof(Info), new {storeId});
    }

    [HttpGet("swapin")]
    [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SwapIn(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }

    [HttpGet("info")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Info(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }
    [HttpGet("logs")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Logs(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View( client.Events);
    }

    [HttpPost("sweep")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Sweep(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            // In Spark SDK v0.4.1, check for any unclaimed deposits
            var request = new ListUnclaimedDepositsRequest();
            var response = await client.Sdk.ListUnclaimedDeposits(request);

            if (response.deposits.Any())
            {
                TempData[WellKnownTempData.SuccessMessage] = $"Found {response.deposits.Count} unclaimed deposits";
            }
            else
            {
                TempData[WellKnownTempData.SuccessMessage] = "No pending deposits to claim";
            }
        }
        catch (Exception e)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"error claiming deposits: {e.Message}";
        }

        return View((object) storeId);
    }

    [HttpGet("send")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Send(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }   
    [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpGet("receive")]
    [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Receive(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }

    [HttpPost("receive")]
    [Authorize(Policy = Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Receive(string storeId, long? amount, string description)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            description ??= "BTCPay Server Invoice";

            var paymentMethod = new ReceivePaymentMethod.Bolt11Invoice(
                description: description,
                amountSats: amount != null ? (ulong)amount.Value : null
            );

            var request = new ReceivePaymentRequest(paymentMethod: paymentMethod);
            var response = await client.Sdk.ReceivePayment(request: request);

            TempData["bolt11"] = response.paymentRequest;
            TempData[WellKnownTempData.SuccessMessage] = "Invoice created successfully!";

            return RedirectToAction(nameof(Transactions), new {storeId});
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error creating invoice: {ex.Message}";
            return View((object) storeId);
        }
    }

    [HttpPost("prepare-send")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> PrepareSend(string storeId, string address, long? amount)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                TempData[WellKnownTempData.ErrorMessage] = "Payment destination is required";
                return RedirectToAction(nameof(Send), new {storeId});
            }

            var amountSats = ResolveAmountSats(address, amount);

            var prepareRequest = new PrepareSendPaymentRequest(
                paymentRequest: address,
                amount: amountSats
            );

            var prepareResponse = await client.Sdk.PrepareSendPayment(prepareRequest);

            if (prepareResponse.paymentMethod is SendPaymentMethod.Bolt11Invoice bolt11Method)
            {
                var totalFee = bolt11Method.lightningFeeSats + (bolt11Method.sparkTransferFeeSats ?? 0);
                var amt = amountSats ?? BigInteger.Zero;
                ViewData["PaymentDetails"] = new PaymentDetailsDto(
                    Destination: address,
                    Amount: (long)amt,
                    Fee: (long)totalFee
                );
            }
            else if (prepareResponse.paymentMethod is SendPaymentMethod.BitcoinAddress bitcoinMethod)
            {
                var fees = bitcoinMethod.feeQuote;
                var mediumFee = fees.speedMedium.userFeeSat + fees.speedMedium.l1BroadcastFeeSat;
                ViewData["PaymentDetails"] = new PaymentDetailsDto(
                    Destination: address,
                    Amount: (long)BigInteger.Abs(amountSats ?? BigInteger.Zero),
                    Fee: (long)mediumFee
                );
            }
            else if (prepareResponse.paymentMethod is SendPaymentMethod.SparkAddress sparkMethod)
            {
                ViewData["PaymentDetails"] = new PaymentDetailsDto(
                    Destination: address,
                    Amount: (long)BigInteger.Abs(amountSats ?? BigInteger.Zero),
                    Fee: (long)sparkMethod.fee
                );
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error preparing payment: {ex.Message}";
        }

        return View(nameof(Send), storeId);
    }

    [HttpPost("confirm-send")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ConfirmSend(string storeId, string paymentRequest, long amount)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            // Re-run preparation to avoid polymorphic JSON deserialization issues
            var amountSats = ResolveAmountSats(paymentRequest, amount);
            var prepareResponse = await client.Sdk.PrepareSendPayment(new PrepareSendPaymentRequest(
                paymentRequest: paymentRequest,
                amount: amountSats
            ));

            SendPaymentOptions? options = prepareResponse.paymentMethod switch
            {
                SendPaymentMethod.Bolt11Invoice => new SendPaymentOptions.Bolt11Invoice(
                    preferSpark: false,
                    completionTimeoutSecs: 60
                ),
                SendPaymentMethod.BitcoinAddress => new SendPaymentOptions.BitcoinAddress(
                    confirmationSpeed: OnchainConfirmationSpeed.Medium
                ),
                SendPaymentMethod.SparkAddress => null,
                SendPaymentMethod.SparkInvoice => null,
                _ => null
            };

            var sendRequest = new SendPaymentRequest(
                prepareResponse: prepareResponse,
                options: options
            );

            _logger.LogInformation("BreezSpark sending payment for store {StoreId} to {Destination}", storeId, paymentRequest);
            var sendResponse = await client.Sdk.SendPayment(sendRequest);
            _logger.LogInformation("BreezSpark send complete for store {StoreId}: payment id {PaymentId}, status {Status}",
                storeId, sendResponse.payment?.id, sendResponse.payment?.status);

            TempData[WellKnownTempData.SuccessMessage] = "Payment sent successfully!";
            return RedirectToAction(nameof(Transactions), new {storeId});
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error sending payment: {ex.Message}";
            _logger.LogError(ex, "BreezSpark send failed for store {StoreId}", storeId);
            return RedirectToAction(nameof(Send), new {storeId});
        }
    }


    [HttpGet("swapout")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SwapOut(string storeId)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }

    [HttpPost("swapout")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SwapOut(string storeId, string address, ulong amount, uint satPerByte,
        string feesHash)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            // Use current SDK pattern for onchain payments
            var prepareRequest = new PrepareSendPaymentRequest(
                paymentRequest: address,
                amount: new BigInteger(amount)
            );

            var prepareResponse = await client.Sdk.PrepareSendPayment(prepareRequest);

            if (prepareResponse.paymentMethod is SendPaymentMethod.BitcoinAddress bitcoinMethod)
            {
                var options = new SendPaymentOptions.BitcoinAddress(
                    confirmationSpeed: OnchainConfirmationSpeed.Medium
                );

                var sendRequest = new SendPaymentRequest(
                    prepareResponse: prepareResponse,
                    options: options
                );

                var sendResponse = await client.Sdk.SendPayment(sendRequest);

                TempData[WellKnownTempData.SuccessMessage] = "Onchain payment initiated successfully!";
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = "Invalid payment method for onchain swap";
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error processing swap-out: {ex.Message}";
        }

        return RedirectToAction(nameof(SwapOut), new {storeId});
    }

    [HttpGet("swapin/{address}/refund")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SwapInRefund(string storeId, string address)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        return View((object) storeId);
    }

    [HttpPost("swapin/{address}/refund")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SwapInRefund(string storeId, string txid, uint vout, string refundAddress, uint? satPerByte = null)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        try
        {
            // Parse the txid:vout format from depositId if needed
            var fee = new Fee.Rate((ulong)(satPerByte ?? 5m));
            var request = new RefundDepositRequest(
                txid: txid,
                vout: vout,
                destinationAddress: refundAddress,
                fee: fee
            );

            var resp = await client.Sdk.RefundDeposit(request);
            TempData[WellKnownTempData.SuccessMessage] = $"Refund successful: {resp.txId}";
        }
        catch (Exception e)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldnt refund: {e.Message}";
        }

        return RedirectToAction(nameof(SwapIn), new {storeId});
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [HttpGet("configure")]
    public async Task<IActionResult> Configure(string storeId)
    {
        return View(await _breezService.Get(storeId));
    }
    [HttpPost("configure")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string command, BreezSparkSettings settings)
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
        {
            return NotFound();
        }
        var pmi = new PaymentMethodId("BTC-LN");
        // In v2.2.1, payment methods are handled differently
        // TODO: Implement proper v2.2.1 payment method handling
        if (command == "clear")
        {
            await _breezService.Set(storeId, null);
            TempData[WellKnownTempData.SuccessMessage] = "Settings cleared successfully";
            var client = _breezService.GetClient(storeId);
            // In v2.2.1, payment methods are handled differently
            // TODO: Implement proper v2.2.1 payment method handling
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        if (command == "save")
        {
            try
            {
                if (string.IsNullOrEmpty(settings.Mnemonic))
                {
                    ModelState.AddModelError(nameof(settings.Mnemonic), "Mnemonic is required");
                    return View(settings);
                }

                try
                {
                    new Mnemonic(settings.Mnemonic);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(settings.Mnemonic), "Invalid mnemonic");
                    return View(settings);
                }

                await _breezService.Set(storeId, settings);
            }
            catch (Exception e)
            {
                TempData[WellKnownTempData.ErrorMessage] = $"Couldnt use provided settings: {e.Message}";
                return View(settings);
            }

            // In v2.2.1, payment methods are handled differently
            // TODO: Implement proper v2.2.1 payment method handling
            // This will require a complete rewrite of the payment method system

            TempData[WellKnownTempData.SuccessMessage] = "Settings saved successfully";
            return RedirectToAction(nameof(Info), new {storeId});
        }

        return NotFound();
    }

    [Route("transactions")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Transactions(string storeId, PaymentsViewModel viewModel)
    {
        var client = _breezService.GetClient(storeId);
        if (client is null)
        {
            return RedirectToAction(nameof(Configure), new {storeId});
        }

        viewModel ??= new PaymentsViewModel();
        viewModel.Balance = await client.GetBalance();
        var req = new ListPaymentsRequest(
            typeFilter: null,
            statusFilter: null,
            assetFilter: new AssetFilter.Bitcoin(),
            fromTimestamp: null,
            toTimestamp: null,
            offset: viewModel.Skip > 0 ? (uint?)viewModel.Skip : null,
            limit: viewModel.Count > 0 ? (uint?)viewModel.Count : null,
            sortAscending: false
        );
        var response = await client.Sdk.ListPayments(req);
        var normalized = new List<NormalizedPayment>();
        foreach (var p in response.payments.Where(p => p != null))
        {
            var norm = client.NormalizePayment(p);
            if (norm is not null)
            {
                normalized.Add(norm);
                continue;
            }

            // Fallback: show raw SDK payment even if we lack invoice context
            long amountSat = 0;
            if (p.details is PaymentDetails.Lightning l && !string.IsNullOrEmpty(l.invoice))
            {
                var nbitcoinNetwork = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>("BTC")?.NBitcoinNetwork ?? NBitcoin.Network.Main;
                if (BOLT11PaymentRequest.TryParse(l.invoice, out var pr, nbitcoinNetwork) && pr?.MinimumAmount is not null)
                {
                    amountSat = (long)pr.MinimumAmount.ToUnit(LightMoneyUnit.Satoshi);
                }
            }

            long feeSat = 0;
            if (p.fees != null)
            {
                feeSat = (long)(p.fees / 1000);
            }
            normalized.Add(new NormalizedPayment
            {
                Id = p.id ?? Guid.NewGuid().ToString("N"),
                PaymentType = p.paymentType,
                Status = p.status,
                Timestamp = p.timestamp,
                Amount = LightMoney.Satoshis(amountSat),
                Fee = LightMoney.Satoshis(feeSat),
                Description = p.details?.ToString() ?? "BreezSpark payment"
            });
        }
        viewModel.Payments = normalized;

        return View("Transactions", viewModel);
    }

    private BigInteger? ResolveAmountSats(string paymentRequest, long? amount)
    {
        if (amount.HasValue && amount.Value > 0)
        {
            return new BigInteger(amount.Value);
        }

        // Try to derive amount from bolt11 invoice if present
        var nbitcoinNetwork = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>("BTC")?.NBitcoinNetwork ?? NBitcoin.Network.Main;
        if (BOLT11PaymentRequest.TryParse(paymentRequest, out var pr, nbitcoinNetwork) && pr?.MinimumAmount is not null)
        {
            return new BigInteger((long)pr.MinimumAmount.ToUnit(LightMoneyUnit.Satoshi));
        }

        return null;
    }
}

public class PaymentsViewModel : BasePagingViewModel
{
    public List<NormalizedPayment> Payments { get; set; } = new();
    public LightningNodeBalance? Balance { get; set; }
    public override int CurrentPageCount => Payments.Count;
}

public record PaymentDetailsDto(string Destination, long Amount, long Fee);

// Helper class for swap information display in views
public class SwapInfo
{
    public string? bitcoinAddress { get; set; }
    public ulong minAllowedDeposit { get; set; }
    public ulong maxAllowedDeposit { get; set; }
    public string? status { get; set; }
}

// Helper class for swap limits display in views
public class SwapLimits
{
    public ulong min { get; set; }
    public ulong max { get; set; }
}
