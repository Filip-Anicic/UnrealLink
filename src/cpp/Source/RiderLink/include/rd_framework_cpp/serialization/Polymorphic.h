//
// Created by jetbrains on 30.07.2018.
//

#ifndef RD_CPP_POLYMORPHIC_H
#define RD_CPP_POLYMORPHIC_H

#include "ISerializer.h"
#include "RdReactiveBase.h"

#include <type_traits>


template<typename T, typename R = void>
class Polymorphic {
public:
    static T read(SerializationCtx const &ctx, Buffer const &buffer) {
        return T::read(ctx, buffer);
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, T const &value) {
        value.write(ctx, buffer);
    }
};


template<typename T>
class Polymorphic<T, typename std::enable_if<std::is_integral<T>::value>::type> {
public:
    static T read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.read_pod<T>();
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, T const &value) {
        buffer.write_pod<T>(value);
    }

};

//class Polymorphic<int, void>;

template<typename T>
class Polymorphic<std::vector<T>> {
public:
    static std::vector<T> read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.readArray<T>();
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, std::vector<T> const &value) {
        buffer.writeArray<T>(value);
    }
};

/*template<>
class Polymorphic<std::string> {
public:
    static std::string read(SerializationCtx const &ctx, Buffer const &buffer) {
        assert("use std::wstring instead of std::string" && 0);
        //        return buffer.readString();
     }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, std::string const &value) {
        assert("use std::wstring instead of std::string" && 0);
        //        buffer.writeString(value);
    }
};*/

template<>
class Polymorphic<std::wstring> {
public:
    static std::wstring read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.readWString();
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, std::wstring const &value) {
        buffer.writeWString(value);
    }
};

template<>
class Polymorphic<void *> {
public:
    static void *read(SerializationCtx const &ctx, Buffer const &buffer) {
        return nullptr;
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, void *const &value) {}
};

template<typename T>
class Polymorphic<T, typename std::enable_if<std::is_base_of<RdReactiveBase, T>::value>::type> {
public:
    static T read(SerializationCtx const &ctx, Buffer const &buffer) {
        return T::read(ctx, buffer);
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, T const &value) {
        value.write(ctx, buffer);
    }
};

template<typename T>
class Polymorphic<T, typename std::enable_if<std::is_enum<T>::value>::type> {
public:
    static T read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.readEnum<T>();
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, T const &value) {
        buffer.writeEnum<T>(value);
    }
};

template<typename T>
class Polymorphic<tl::optional<T>> {
public:
    static tl::optional<T> read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.readNullable<T>([&ctx, &buffer]() {
            return Polymorphic<T>::read(ctx, buffer);
        });
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, tl::optional<T> const &value) {
        buffer.writeNullable<T>(value, [&ctx, &buffer](T const &v) {
            Polymorphic<T>::write(ctx, buffer, v);
        });
    }
};

/*template<>
class Polymorphic<tl::optional<std::wstring>> {
    using T = std::wstring;
public:
    static tl::optional<T> read(SerializationCtx const &ctx, Buffer const &buffer) {
        return buffer.readNullableWString();
    }

    static void write(SerializationCtx const &ctx, Buffer const &buffer, tl::optional<T> const &value) {
        buffer.writeNullableWString(value);
    }
};*/

#endif //RD_CPP_POLYMORPHIC_H
