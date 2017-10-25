

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GoCardless.Internals;
using GoCardless.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GoCardless.Services
{
    /// <summary>
    /// Service class for working with subscription resources.
    ///
    /// Subscriptions create [payments](#core-endpoints-payments) according to a
    /// schedule.
    /// 
    /// ### Recurrence Rules
    /// 
    /// The following rules apply when specifying recurrence:
    /// 
    /// - The first payment must be charged within 1 year.
    /// - When neither `month` nor `day_of_month` are present, the subscription
    /// will recur from the `start_date` based on the `interval_unit`.
    /// - If `month` or `day_of_month` are present, the recurrence rules will be
    /// applied from the `start_date`, and the following validations apply:
    /// 
    /// | interval_unit   | month                                          |
    /// day_of_month                            |
    /// | :-------------- | :--------------------------------------------- |
    /// :-------------------------------------- |
    /// | yearly          | optional (required if `day_of_month` provided) |
    /// optional (required if `month` provided) |
    /// | monthly         | invalid                                        |
    /// required                                |
    /// | weekly          | invalid                                        |
    /// invalid                                 |
    /// 
    /// Examples:
    /// 
    /// | interval_unit   | interval   | month   | day_of_month   | valid?      
    ///                                       |
    /// | :-------------- | :--------- | :------ | :------------- |
    /// :------------------------------------------------- |
    /// | yearly          | 1          | january | -1             | valid       
    ///                                       |
    /// | yearly          | 1          | march   |                | invalid -
    /// missing `day_of_month`                   |
    /// | monthly         | 6          |         | 12             | valid       
    ///                                       |
    /// | monthly         | 6          | august  | 12             | invalid -
    /// `month` must be blank                    |
    /// | weekly          | 2          |         |                | valid       
    ///                                       |
    /// | weekly          | 2          | october | 10             | invalid -
    /// `month` and `day_of_month` must be blank |
    /// 
    /// ### Rolling dates
    /// 
    /// When a charge date falls on a non-business day, one of two things will
    /// happen:
    /// 
    /// - if the recurrence rule specified `-1` as the `day_of_month`, the
    /// charge date will be rolled __backwards__ to the previous business day
    /// (i.e., the last working day of the month).
    /// - otherwise the charge date will be rolled __forwards__ to the next
    /// business day.
    /// 
    /// </summary>

    public class SubscriptionService
    {
        private readonly GoCardlessClient _goCardlessClient;

        /// <summary>
        /// Constructor. Users of this library should not call this. An instance of this
        /// class can be accessed through an initialised GoCardlessClient.
        /// </summary>
        public SubscriptionService(GoCardlessClient goCardlessClient)
        {
            _goCardlessClient = goCardlessClient;
        }

        /// <summary>
        /// Creates a new subscription object
        /// </summary>
        /// <param name="request">An optional `SubscriptionCreateRequest` representing the body for this create request.</param>
        /// <param name="customiseRequestMessage">An optional `RequestSettings` allowing you to configure the request</param>
        /// <returns>A single subscription resource</returns>
        public Task<SubscriptionResponse> CreateAsync(SubscriptionCreateRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionCreateRequest();

            var urlParams = new List<KeyValuePair<string, object>>
            {};

            return _goCardlessClient.ExecuteAsync<SubscriptionResponse>("POST", "/subscriptions", urlParams, request, id => GetAsync(id, null, customiseRequestMessage), "subscriptions", customiseRequestMessage);
        }

        /// <summary>
        /// Returns a [cursor-paginated](#api-usage-cursor-pagination) list of
        /// your subscriptions.
        /// </summary>
        /// <param name="request">An optional `SubscriptionListRequest` representing the query parameters for this list request.</param>
        /// <param name="customiseRequestMessage">An optional `RequestSettings` allowing you to configure the request</param>
        /// <returns>A set of subscription resources</returns>
        public Task<SubscriptionListResponse> ListAsync(SubscriptionListRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionListRequest();

            var urlParams = new List<KeyValuePair<string, object>>
            {};

            return _goCardlessClient.ExecuteAsync<SubscriptionListResponse>("GET", "/subscriptions", urlParams, request, null, null, customiseRequestMessage);
        }

        /// <summary>
        /// Get a lazily enumerated list of subscriptions.
        /// This acts like the #list method, but paginates for you automatically.
        /// </summary>
        public IEnumerable<Subscription> All(SubscriptionListRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionListRequest();

            string cursor = null;
            do
            {
                request.After = cursor;

                var result = Task.Run(() => ListAsync(request, customiseRequestMessage)).Result;
                foreach (var item in result.Subscriptions)
                {
                    yield return item;
                }
                cursor = result.Meta?.Cursors?.After;
            } while (cursor != null);
        }

        /// <summary>
        /// Get a lazily enumerated list of subscriptions.
        /// This acts like the #list method, but paginates for you automatically.
        /// </summary>
        public IEnumerable<Task<IReadOnlyList<Subscription>>> AllAsync(SubscriptionListRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionListRequest();

            return new TaskEnumerable<IReadOnlyList<Subscription>, string>(async after =>
            {
                request.After = after;
                var list = await this.ListAsync(request, customiseRequestMessage);
                return Tuple.Create(list.Subscriptions, list.Meta?.Cursors?.After);
            });
        }

        /// <summary>
        /// Retrieves the details of a single subscription.
        /// </summary>
        /// <param name="identity">Unique identifier, beginning with "SB".</param>
        /// <param name="request">An optional `SubscriptionGetRequest` representing the query parameters for this get request.</param>
        /// <param name="customiseRequestMessage">An optional `RequestSettings` allowing you to configure the request</param>
        /// <returns>A single subscription resource</returns>
        public Task<SubscriptionResponse> GetAsync(string identity, SubscriptionGetRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionGetRequest();
            if (identity == null) throw new ArgumentException(nameof(identity));

            var urlParams = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("identity", identity),
            };

            return _goCardlessClient.ExecuteAsync<SubscriptionResponse>("GET", "/subscriptions/:identity", urlParams, request, null, null, customiseRequestMessage);
        }

        /// <summary>
        /// Updates a subscription object.
        /// 
        /// This fails with:
        /// 
        /// - `subscription_not_active` if the subscription is no longer active.
        /// 
        /// - `subscription_already_ended` if the subscription has taken all
        /// payments.
        /// 
        /// - `mandate_payments_require_approval` if the amount is being changed
        /// and the mandate requires approval.
        /// 
        /// - `exceeded_max_amendments` error if the amount is being changed and
        /// the
        ///   subscription amount has already been changed 10 times.
        /// 
        /// </summary>
        /// <param name="identity">Unique identifier, beginning with "SB".</param>
        /// <param name="request">An optional `SubscriptionUpdateRequest` representing the body for this update request.</param>
        /// <param name="customiseRequestMessage">An optional `RequestSettings` allowing you to configure the request</param>
        /// <returns>A single subscription resource</returns>
        public Task<SubscriptionResponse> UpdateAsync(string identity, SubscriptionUpdateRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionUpdateRequest();
            if (identity == null) throw new ArgumentException(nameof(identity));

            var urlParams = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("identity", identity),
            };

            return _goCardlessClient.ExecuteAsync<SubscriptionResponse>("PUT", "/subscriptions/:identity", urlParams, request, null, "subscriptions", customiseRequestMessage);
        }

        /// <summary>
        /// Immediately cancels a subscription; no more payments will be created
        /// under it. Any metadata supplied to this endpoint will be stored on
        /// the payment cancellation event it causes.
        /// 
        /// This will fail with a cancellation_failed error if the subscription
        /// is already cancelled or finished.
        /// </summary>
        /// <param name="identity">Unique identifier, beginning with "SB".</param>
        /// <param name="request">An optional `SubscriptionCancelRequest` representing the body for this cancel request.</param>
        /// <param name="customiseRequestMessage">An optional `RequestSettings` allowing you to configure the request</param>
        /// <returns>A single subscription resource</returns>
        public Task<SubscriptionResponse> CancelAsync(string identity, SubscriptionCancelRequest request = null, RequestSettings customiseRequestMessage = null)
        {
            request = request ?? new SubscriptionCancelRequest();
            if (identity == null) throw new ArgumentException(nameof(identity));

            var urlParams = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("identity", identity),
            };

            return _goCardlessClient.ExecuteAsync<SubscriptionResponse>("POST", "/subscriptions/:identity/actions/cancel", urlParams, request, null, "data", customiseRequestMessage);
        }
    }

        
    /// <summary>
    /// Creates a new subscription object
    /// </summary>
    public class SubscriptionCreateRequest : IHasIdempotencyKey
    {

        /// <summary>
        /// Amount in pence (GBP), cents (EUR), or öre (SEK).
        /// </summary>
        [JsonProperty("amount")]
        public int? Amount { get; set; }

        /// <summary>
        /// The amount to be deducted from the payment as the OAuth app's fee,
        /// in pence (GBP), cents (EUR), or öre (SEK).
        /// </summary>
        [JsonProperty("app_fee")]
        public int? AppFee { get; set; }

        /// <summary>
        /// The total number of payments that should be taken by this
        /// subscription.
        /// </summary>
        [JsonProperty("count")]
        public int? Count { get; set; }

        /// <summary>
        /// [ISO 4217](http://en.wikipedia.org/wiki/ISO_4217) currency code.
        /// Currently only `GBP`, `EUR`, and `SEK` are supported.
        /// </summary>
        [JsonProperty("currency")]
        public string Currency { get; set; }

        /// <summary>
        /// As per RFC 2445. The day of the month to charge customers on.
        /// `1`-`28` or `-1` to indicate the last day of the month.
        /// </summary>
        [JsonProperty("day_of_month")]
        public int? DayOfMonth { get; set; }

        /// <summary>
        /// Date on or after which no further payments should be created. If
        /// this field is blank and `count` is not specified, the subscription
        /// will continue forever. <p
        /// class='deprecated-notice'><strong>Deprecated</strong>: This field
        /// will be removed in a future API version. Use `count` to specify a
        /// number of payments instead. </p>
        /// </summary>
        [JsonProperty("end_date")]
        public string EndDate { get; set; }

        /// <summary>
        /// Number of `interval_units` between customer charge dates. Must
        /// result in at least one charge date per year. Defaults to `1`.
        /// </summary>
        [JsonProperty("interval")]
        public int? Interval { get; set; }

        /// <summary>
        /// The unit of time between customer charge dates. One of `weekly`,
        /// `monthly` or `yearly`.
        /// </summary>
        [JsonProperty("interval_unit")]
        public SubscriptionIntervalUnit? IntervalUnit { get; set; }
            
        /// <summary>
        /// The unit of time between customer charge dates. One of `weekly`,
        /// `monthly` or `yearly`.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum SubscriptionIntervalUnit
        {
    
            /// <summary>`interval_unit` with a value of "weekly"</summary>
            [EnumMember(Value = "weekly")]
            Weekly,
            /// <summary>`interval_unit` with a value of "monthly"</summary>
            [EnumMember(Value = "monthly")]
            Monthly,
            /// <summary>`interval_unit` with a value of "yearly"</summary>
            [EnumMember(Value = "yearly")]
            Yearly,
        }

        /// <summary>
        /// Linked resources.
        /// </summary>
        [JsonProperty("links")]
        public SubscriptionLinks Links { get; set; }
        /// <summary>
        /// Linked resources for a Subscription.
        /// </summary>
        public class SubscriptionLinks
        {

            /// <summary>
            /// ID of the associated [mandate](#core-endpoints-mandates) which
            /// the subscription will create payments against.
            /// </summary>
            [JsonProperty("mandate")]
            public string Mandate { get; set; }
        }

        /// <summary>
        /// Key-value store of custom data. Up to 3 keys are permitted, with key
        /// names up to 50 characters and values up to 500 characters.
        /// </summary>
        [JsonProperty("metadata")]
        public IDictionary<String, String> Metadata { get; set; }

        /// <summary>
        /// Name of the month on which to charge a customer. Must be lowercase.
        /// </summary>
        [JsonProperty("month")]
        public SubscriptionMonth? Month { get; set; }
            
        /// <summary>
        /// Name of the month on which to charge a customer. Must be lowercase.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum SubscriptionMonth
        {
    
            /// <summary>`month` with a value of "january"</summary>
            [EnumMember(Value = "january")]
            January,
            /// <summary>`month` with a value of "february"</summary>
            [EnumMember(Value = "february")]
            February,
            /// <summary>`month` with a value of "march"</summary>
            [EnumMember(Value = "march")]
            March,
            /// <summary>`month` with a value of "april"</summary>
            [EnumMember(Value = "april")]
            April,
            /// <summary>`month` with a value of "may"</summary>
            [EnumMember(Value = "may")]
            May,
            /// <summary>`month` with a value of "june"</summary>
            [EnumMember(Value = "june")]
            June,
            /// <summary>`month` with a value of "july"</summary>
            [EnumMember(Value = "july")]
            July,
            /// <summary>`month` with a value of "august"</summary>
            [EnumMember(Value = "august")]
            August,
            /// <summary>`month` with a value of "september"</summary>
            [EnumMember(Value = "september")]
            September,
            /// <summary>`month` with a value of "october"</summary>
            [EnumMember(Value = "october")]
            October,
            /// <summary>`month` with a value of "november"</summary>
            [EnumMember(Value = "november")]
            November,
            /// <summary>`month` with a value of "december"</summary>
            [EnumMember(Value = "december")]
            December,
        }

        /// <summary>
        /// Optional name for the subscription. This will be set as the
        /// description on each payment created. Must not exceed 255 characters.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// An optional payment reference. This will be set as the reference on
        /// each payment created and will appear on your customer's bank
        /// statement. See the documentation for the [create payment
        /// endpoint](#payments-create-a-payment) for more details. <p
        /// class='restricted-notice'><strong>Restricted</strong>: You need your
        /// own Service User Number to specify a payment reference for Bacs
        /// payments.</p>
        /// </summary>
        [JsonProperty("payment_reference")]
        public string PaymentReference { get; set; }

        /// <summary>
        /// The date on which the first payment should be charged. Must be
        /// within one year of creation and on or after the
        /// [mandate](#core-endpoints-mandates)'s `next_possible_charge_date`.
        /// When blank, this will be set as the mandate's
        /// `next_possible_charge_date`.
        /// </summary>
        [JsonProperty("start_date")]
        public string StartDate { get; set; }

        /// <summary>
        /// A unique key to ensure that this request only succeeds once, allowing you to safely retry request errors such as network failures.
        /// Any requests, where supported, to create a resource with a key that has previously been used will not succeed.
        /// See: https://developer.gocardless.com/api-reference/#making-requests-idempotency-keys
        /// </summary>
        [JsonIgnore]
        public string IdempotencyKey { get; set; }
    }

        
    /// <summary>
    /// Returns a [cursor-paginated](#api-usage-cursor-pagination) list of your
    /// subscriptions.
    /// </summary>
    public class SubscriptionListRequest
    {

        /// <summary>
        /// Cursor pointing to the start of the desired set.
        /// </summary>
        [JsonProperty("after")]
        public string After { get; set; }

        /// <summary>
        /// Cursor pointing to the end of the desired set.
        /// </summary>
        [JsonProperty("before")]
        public string Before { get; set; }

        /// <summary>
        /// Limit to records created within certain times.
        /// </summary>
        [JsonProperty("created_at")]
        public CreatedAtParam CreatedAt { get; set; }

        /// <summary>
        /// Specify filters to limit records by creation time.
        /// </summary>
        public class CreatedAtParam
        {
            /// <summary>
            /// Limit to records created after the specified date-time.
            /// </summary>
            [JsonProperty("gt")]
            public DateTimeOffset? GreaterThan { get; set; }

            /// <summary>
            /// Limit to records created on or after the specified date-time.
            /// </summary>
            [JsonProperty("gte")]
            public DateTimeOffset? GreaterThanOrEqual { get; set; }

            /// <summary>
            /// Limit to records created before the specified date-time.
            /// </summary>
            [JsonProperty("lt")]
            public DateTimeOffset? LessThan { get; set; }

            /// <summary>
            ///Limit to records created on or before the specified date-time.
            /// </summary>
            [JsonProperty("lte")]
            public DateTimeOffset? LessThanOrEqual { get; set; }
        }

        /// <summary>
        /// Unique identifier, beginning with "CU".
        /// </summary>
        [JsonProperty("customer")]
        public string Customer { get; set; }

        /// <summary>
        /// Number of records to return.
        /// </summary>
        [JsonProperty("limit")]
        public int? Limit { get; set; }

        /// <summary>
        /// Unique identifier, beginning with "MD".
        /// </summary>
        [JsonProperty("mandate")]
        public string Mandate { get; set; }
    }

        
    /// <summary>
    /// Retrieves the details of a single subscription.
    /// </summary>
    public class SubscriptionGetRequest
    {
    }

        
    /// <summary>
    /// Updates a subscription object.
    /// 
    /// This fails with:
    /// 
    /// - `subscription_not_active` if the subscription is no longer active.
    /// 
    /// - `subscription_already_ended` if the subscription has taken all
    /// payments.
    /// 
    /// - `mandate_payments_require_approval` if the amount is being changed and
    /// the mandate requires approval.
    /// 
    /// - `exceeded_max_amendments` error if the amount is being changed and the
    ///   subscription amount has already been changed 10 times.
    /// 
    /// </summary>
    public class SubscriptionUpdateRequest
    {

        /// <summary>
        /// Amount in pence (GBP), cents (EUR), or öre (SEK).
        /// </summary>
        [JsonProperty("amount")]
        public int? Amount { get; set; }

        /// <summary>
        /// Key-value store of custom data. Up to 3 keys are permitted, with key
        /// names up to 50 characters and values up to 500 characters.
        /// </summary>
        [JsonProperty("metadata")]
        public IDictionary<String, String> Metadata { get; set; }

        /// <summary>
        /// Optional name for the subscription. This will be set as the
        /// description on each payment created. Must not exceed 255 characters.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// An optional payment reference. This will be set as the reference on
        /// each payment created and will appear on your customer's bank
        /// statement. See the documentation for the [create payment
        /// endpoint](#payments-create-a-payment) for more details. <p
        /// class='restricted-notice'><strong>Restricted</strong>: You need your
        /// own Service User Number to specify a payment reference for Bacs
        /// payments.</p>
        /// </summary>
        [JsonProperty("payment_reference")]
        public string PaymentReference { get; set; }
    }

        
    /// <summary>
    /// Immediately cancels a subscription; no more payments will be created
    /// under it. Any metadata supplied to this endpoint will be stored on the
    /// payment cancellation event it causes.
    /// 
    /// This will fail with a cancellation_failed error if the subscription is
    /// already cancelled or finished.
    /// </summary>
    public class SubscriptionCancelRequest
    {

        /// <summary>
        /// Key-value store of custom data. Up to 3 keys are permitted, with key
        /// names up to 50 characters and values up to 500 characters.
        /// </summary>
        [JsonProperty("metadata")]
        public IDictionary<String, String> Metadata { get; set; }
    }

    /// <summary>
    /// An API response for a request returning a single subscription.
    /// </summary>
    public class SubscriptionResponse : ApiResponse
    {
        /// <summary>
        /// The subscription from the response.
        /// </summary>
        [JsonProperty("subscriptions")]
        public Subscription Subscription { get; private set; }
    }

    /// <summary>
    /// An API response for a request returning a list of subscriptions.
    /// </summary>
    public class SubscriptionListResponse : ApiResponse
    {
        /// <summary>
        /// The list of subscriptions from the response.
        /// </summary>
        public IReadOnlyList<Subscription> Subscriptions { get; private set; }

        /// <summary>
        /// Response metadata (e.g. pagination cursors)
        /// </summary>
        public Meta Meta { get; private set; }
    }
}
