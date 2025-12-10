# BTCPayServer Breez Plugin

A BTCPayServer plugin that enables Lightning payments using Breez's nodeless SDK (Spark) as a Lightning backend, eliminating the need to run a full Lightning node.

## Overview

This plugin allows BTCPayServer merchants to accept Lightning payments through Breez's non-custodial, nodeless Lightning infrastructure. Instead of maintaining a Lightning node with liquidity and channel management, merchants can use Spark based.

## Features

- **Nodeless Lightning**: No need to run or maintain a Lightning node, instead use Spark based swaps 
- **Non-custodial**: You maintain control over your funds (on Spark)

## How It Works

1. **Invoice Creation**: When creating a Lightning invoice in BTCPayServer, the plugin uses Breez SDK to generate a BOLT11 invoice
2. **Payment Detection**: A background service monitors for payment events through the Breez SDK
3. **Status Updates**: Detected payments are automatically reported back to BTCPayServer, updating invoice status from "new" to "paid"

## Installation

Download and install the plugin from the GitHub releases:
https://github.com/aljazceru/btcpayserver-breez-nodeless-plugin/releases


## Configuration

Connect your BTCPayServer instance to Breez using a connection string:
```
type=breez;key=<your_payment_key>
```

## Requirements

- BTCPayServer instance
- BreezSDK API key


