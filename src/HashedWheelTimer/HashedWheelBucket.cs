using System;
using System.Collections.Generic;

namespace HashedWheelTimer
{
    internal sealed class HashedWheelBucket
    {
        // Used for the linked-list datastructure
        private HashedWheelTimeout _head;
        private HashedWheelTimeout _tail;

        public void AddTimeout(HashedWheelTimeout timeout)
        {
            timeout.Bucket = this;
            if (_head == null)
            {
                _head = _tail = timeout;
            }
            else
            {
                _tail.Next = timeout;
                timeout.Prev = _tail;
                _tail = timeout;
            }
        }

        public void ExpireTimeouts(long deadline)
        {
            var timeout = _head;

            // process all timeouts
            while (timeout != null)
            {
                var next = timeout.Next;
                if (timeout.RemainingRounds <= 0)
                {
                    next = Remove(timeout);
                    if (timeout.Deadline <= deadline)
                    {
                        timeout.Expire();
                    }
                    else
                    {
                        // The timeout was placed into a wrong slot. This should never happen.
                        throw new InvalidOperationException(
                            $"timeout.deadline ({timeout.Deadline}) > deadline ({deadline})");
                    }
                }
                else if (timeout.Cancelled)
                {
                    next = Remove(timeout);
                }
                else
                {
                    timeout.RemainingRounds--;
                }

                timeout = next;
            }
        }

        public HashedWheelTimeout Remove(HashedWheelTimeout timeout)
        {
            var next = timeout.Next;
            // remove timeout that was either processed or cancelled by updating the linked-list
            if (timeout.Prev != null)
            {
                timeout.Prev.Next = next;
            }

            if (timeout.Next != null)
            {
                timeout.Next.Prev = timeout.Prev;
            }

            if (timeout == _head)
            {
                // if timeout is also the tail we need to adjust the entry too
                if (timeout == _tail)
                {
                    _tail = null;
                    _head = null;
                }
                else
                {
                    _head = next;
                }
            }
            else if (timeout == _tail)
            {
                // if the timeout is the tail modify the tail to be the prev node.
                _tail = timeout.Prev;
            }

            // null out prev, next and bucket to allow for GC.
            timeout.Prev = null;
            timeout.Next = null;
            timeout.Bucket = null;
            timeout.DecrementPendingTimeouts();
            return next;
        }

        public void ClearTimeouts(ISet<ITimeout> set)
        {
            for (;;)
            {
                var timeout = PollTimeout();
                if (timeout == null)
                {
                    return;
                }

                if (timeout.Expired || timeout.Cancelled)
                {
                    continue;
                }

                set.Add(timeout);
            }
        }

        private HashedWheelTimeout PollTimeout()
        {
            var head = _head;
            if (head == null)
            {
                return null;
            }

            var next = head.Next;
            if (next == null)
            {
                _tail = _head = null;
            }
            else
            {
                _head = next;
                next.Prev = null;
            }

            // null out prev and next to allow for GC.
            head.Next = null;
            head.Prev = null;
            head.Bucket = null;
            return head;
        }
    }
}