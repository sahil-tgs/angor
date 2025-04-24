// Location: src/Angor/Shared/Liquid/LiquidNetworkException.cs
// (You will need to create the 'Liquid' subdirectory within 'src/Angor/Shared/')

using System;

namespace Angor.Shared.Liquid
{
    /// <summary>
    /// Represents specific error conditions encountered during Liquid Network interactions.
    /// </summary>
    public enum LiquidErrorCode
    {
        // Network/RPC Errors
        ConnectionFailed,
        RpcError,          // General error returned by node/API
        Timeout,
        InvalidResponse,   // Unexpected response format from node/API

        // Transaction Errors
        TransactionBuildFailed,
        TransactionSignFailed,
        TransactionBroadcastFailed, // Specific broadcast failure
        MempoolConflict,          // e.g., Tx already seen, double-spend attempt, rule violation
        TxNotFound,               // When querying a transaction that doesn't exist

        // Asset/Data Errors
        AssetInfoNotFound,        // Could not retrieve info for a given Asset ID
        InvalidLiquidAddress,

        // Wallet/Funds Errors
        InsufficientFunds,        // Could include AssetId and Amount needed later
        InsufficientLbtcForFee,

        // Other
        ConfidentialTxError,      // Specific error related to Confidential Transactions
        Unknown                   // Default for unexpected errors
    }

    /// <summary>
    /// Custom exception class for Liquid Network specific errors.
    /// Carries a specific error code and optional details.
    /// </summary>
    public class LiquidNetworkException : Exception
    {
        /// <summary>
        /// Gets the specific code identifying the type of Liquid network error.
        /// </summary>
        public LiquidErrorCode ErrorCode { get; }

        /// <summary>
        /// Gets optional additional details about the error (e.g., RPC error message, HTTP status).
        /// </summary>
        public string? Details { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LiquidNetworkException"/> class.
        /// </summary>
        /// <param name="code">The specific error code.</param>
        /// <param name="message">The primary error message.</param>
        /// <param name="details">Optional additional details.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public LiquidNetworkException(LiquidErrorCode code, string message, string? details = null, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = code;
            Details = details;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LiquidNetworkException"/> class without details.
        /// </summary>
        /// <param name="code">The specific error code.</param>
        /// <param name="message">The primary error message.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public LiquidNetworkException(LiquidErrorCode code, string message, Exception? innerException = null)
            : this(code, message, null, innerException)
        { }
    }
}
