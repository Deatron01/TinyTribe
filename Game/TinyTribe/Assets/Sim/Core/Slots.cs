using System;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// Stack arithmetic shared by villager packs, building storage and item piles.
    /// A set of slots is an array of stacks of which the first `capacity` are usable.
    /// </summary>
    public static class Slots
    {
        public static int Count(ItemStack[] slots, int capacity, int item)
        {
            int total = 0;
            for (int i = 0; i < capacity; i++)
                if (slots[i].Qty > 0 && slots[i].Item == item) total += slots[i].Qty;
            return total;
        }

        public static int Free(ItemStack[] slots, int capacity)
        {
            int free = 0;
            for (int i = 0; i < capacity; i++)
                if (slots[i].Qty <= 0) free++;
            return free;
        }

        /// <summary>First slot holding the item, or -1.</summary>
        public static int Find(ItemStack[] slots, int capacity, int item)
        {
            for (int i = 0; i < capacity; i++)
                if (slots[i].Qty > 0 && slots[i].Item == item) return i;
            return -1;
        }

        /// <summary>How many more of the item fit: room in its stacks plus every empty slot.</summary>
        public static int Room(ItemStack[] slots, int capacity, int item, int maxStack)
        {
            int room = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Qty <= 0) room += maxStack;
                else if (slots[i].Item == item) room += maxStack - slots[i].Qty;
            }
            return room;
        }

        /// <summary>Adds up to qty, topping up existing stacks first. Returns how many fitted.</summary>
        public static int Add(ItemStack[] slots, int capacity, int item, int qty, int maxStack, int durability)
        {
            int left = qty;
            for (int i = 0; i < capacity && left > 0 && maxStack > 1; i++)
            {
                if (slots[i].Qty <= 0 || slots[i].Item != item) continue;
                int take = Math.Min(left, maxStack - slots[i].Qty);
                slots[i].Qty += take;
                left -= take;
            }
            for (int i = 0; i < capacity && left > 0; i++)
            {
                if (slots[i].Qty > 0) continue;
                int take = Math.Min(left, maxStack);
                slots[i] = new ItemStack { Item = item, Qty = take, Durability = durability };
                left -= take;
            }
            return qty - left;
        }

        /// <summary>Removes up to qty, emptying the last stacks first. Returns how many were removed.</summary>
        public static int Remove(ItemStack[] slots, int capacity, int item, int qty)
        {
            int left = qty;
            for (int i = capacity - 1; i >= 0 && left > 0; i--)
            {
                if (slots[i].Qty <= 0 || slots[i].Item != item) continue;
                int take = Math.Min(left, slots[i].Qty);
                slots[i].Qty -= take;
                left -= take;
                if (slots[i].Qty == 0) slots[i] = default;
            }
            return qty - left;
        }
    }
}
